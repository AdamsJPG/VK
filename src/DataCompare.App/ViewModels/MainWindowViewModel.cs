using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataCompare.Engine.Connections;
using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Models;
using DataCompare.Engine.Profiles;
using DataCompare.Engine.Reporting;
using DataCompare.Engine.Schema;
using DataCompare.Engine.Security;
using Microsoft.Data.SqlClient;

namespace DataCompare.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    // No exclusion rules exist yet (that's phase 4) — data comparison compares every common column
    // on purpose, so examples reveal which columns are noise and belong in the exclusion list.
    private const int MaxDiscrepanciesShownPerTable = 10;
    private const int SampleRowsPerDiscrepancy = 3;

    // Tables at or above this row count are range-partitioned across several concurrent workers
    // instead of running as one long single-threaded pass (planning.md §19) — otherwise a handful of
    // huge tables each occupy one parallelism slot for their entire duration while everything else
    // finishes and slots sit idle. Their chunk jobs run in the SAME Parallel.ForEachAsync call as
    // every small table, not a separate later phase — an earlier version deferred them to a second
    // phase that only started once every small table finished, which made a large table's split wait
    // behind whichever small table happened to be slowest, defeating the point.
    private const long LargeTableRowCountThreshold = 1_000_000;
    private const int LargeTablePartitionCount = 5;

    // ⚠ TEMPORARY DEV-ONLY HACK — REMOVE BEFORE SHIPPING ⚠
    // Skips the slowest known tables so local iteration on UI/reporting changes doesn't cost a
    // 20+ minute full run every time. Has nothing to do with correctness or exclusion rules (planning.md
    // §7/§18/§19 are unaffected) — purely a dev-loop speed hack. Set back to [] (or delete this
    // filter entirely) once done iterating.
    private static readonly string[] TemporarilySkippedTablesForFasterIteration =
        ["dbo.InvoiceLine", "dbo.EventLog", "dbo.InvoiceReport"];

    // Not a user-facing profile — there's no Save/Load Profile UI right now (dropped deliberately),
    // so this is the one implicit "remember what I last set up" slot that "Remember credentials"
    // actually needs to mean something: without it, the password gets saved but nothing ever
    // restores server/user/database, so it looks like remembering does nothing at all.
    private const string LastUsedProfileName = "__LastUsed__";

    private readonly ComparisonProfileStore _profileStore;
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly SchemaReader _schemaReader = new();
    private readonly SchemaComparer _schemaComparer = new();
    private readonly KeyedTableComparer _keyedTableComparer = new();
    private readonly TableHashComparer _tableHashComparer = new();
    private readonly RowDrillDownFetcher _drillDownFetcher = new();
    private readonly LargeContentValueFetcher _largeContentValueFetcher = new();
    private readonly TableRowCountReader _rowCountReader = new();
    private readonly TableRangePartitioner _rangePartitioner = new();

    // Per-table connections used during the compare pass are opened and disposed within that pass
    // (see RunDataComparisonAsync) — long gone by the time the user clicks a result row later, so
    // the "Open both" drill-down action needs to remember how to open fresh ones on demand.
    private ConnectionProfile? _lastDataSourceProfile;
    private ConnectionProfile? _lastDataTargetProfile;
    private string? _lastDataSourcePassword;
    private string? _lastDataTargetPassword;

    [ObservableProperty]
    private string _profileName = "Untitled Comparison";

    [ObservableProperty]
    private string _saveStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompareStatusMessage))]
    private string _schemaComparisonStatus = string.Empty;

    [ObservableProperty]
    private ICollectionView? _schemaRowsView;

    [ObservableProperty]
    private SchemaObjectRow? _selectedSchemaRow;

    [ObservableProperty]
    private string _selectedSchemaObjectHeader = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DdlLine> _sourceDdlLines = [];

    [ObservableProperty]
    private ObservableCollection<DdlLine> _targetDdlLines = [];

    private DatabaseSchema? _lastSourceSchema;
    private DatabaseSchema? _lastTargetSchema;
    private SchemaDiffResult? _lastSchemaDiffResult;
    private IReadOnlyList<DataComparisonRow>? _lastDataComparisonRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompareStatusMessage))]
    private string _dataComparisonStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(CompareStatusMessage))]
    private bool _isComparing;

    [ObservableProperty]
    private ObservableCollection<DiffTreeNode> _dataDiffNodes = [];

    [ObservableProperty]
    private ICollectionView? _dataComparisonRowsView;

    [ObservableProperty]
    private DataComparisonRow? _selectedDataComparisonRow;

    [ObservableProperty]
    private ObservableCollection<DiffTreeNode> _selectedDataComparisonDetailNodes = [];

    [ObservableProperty]
    private ObservableCollection<TableProgressItem> _tableProgressItems = [];

    [ObservableProperty]
    private string _elapsedTimeText = string.Empty;

    private readonly Stopwatch _comparisonStopwatch = new();
    private DispatcherTimer? _elapsedTimeTimer;

    public ConnectionSetupViewModel ConnectionA { get; }
    public ConnectionSetupViewModel ConnectionB { get; }

    /// <summary>Raised once both schema and data comparison finish, so the view can show the results window.</summary>
    public event EventHandler? ResultsReady;

    public bool CanCompare => !IsComparing && ConnectionA.HasRequiredFieldsFilled() && ConnectionB.HasRequiredFieldsFilled();

    /// <summary>Text shown next to the Compare button — live progress while a comparison is running,
    /// otherwise an explanation of what's missing (comparing gives no other visible feedback until
    /// the results window opens, which for a real database can take a while).</summary>
    public string CompareStatusMessage
    {
        get
        {
            if (IsComparing)
            {
                return !string.IsNullOrEmpty(DataComparisonStatus) ? DataComparisonStatus : SchemaComparisonStatus;
            }

            if (ConnectionA.HasRequiredFieldsFilled() && ConnectionB.HasRequiredFieldsFilled())
            {
                return string.Empty;
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(ConnectionA.ServerName) || string.IsNullOrWhiteSpace(ConnectionB.ServerName))
            {
                missing.Add("Server");
            }

            if (string.IsNullOrWhiteSpace(ConnectionA.UserId) || string.IsNullOrWhiteSpace(ConnectionB.UserId))
            {
                missing.Add("User name");
            }

            return $"Fill in {string.Join(" and ", missing)} on both sides to enable Compare.";
        }
    }

    public MainWindowViewModel()
        : this(new WindowsCredentialStore(), new ComparisonProfileStore())
    {
    }

    public MainWindowViewModel(ICredentialStore credentialStore, ComparisonProfileStore profileStore)
    {
        _profileStore = profileStore;
        _connectionFactory = new SqlConnectionFactory();

        ConnectionA = new ConnectionSetupViewModel(credentialStore, _connectionFactory) { Label = "Source", Name = "A" };
        ConnectionB = new ConnectionSetupViewModel(credentialStore, _connectionFactory) { Label = "Target", Name = "B", IsTarget = true };

        ConnectionA.PropertyChanged += (_, _) => NotifyCompareStateChanged();
        ConnectionB.PropertyChanged += (_, _) => NotifyCompareStateChanged();

        ConnectionA.ConnectionTestSucceeded += async (_, _) => await SaveLastUsedProfileAsync();
        ConnectionB.ConnectionTestSucceeded += async (_, _) => await SaveLastUsedProfileAsync();
    }

    /// <summary>Restores the last-used server/database/user (and remember-credentials flag) for both
    /// sides, if one was ever saved. Called once from the view after it's shown, so it can also pull
    /// remembered passwords into the PasswordBoxes afterward.</summary>
    public async Task LoadLastUsedProfileAsync()
    {
        var profile = await _profileStore.LoadAsync(LastUsedProfileName);
        if (profile is null)
        {
            return;
        }

        ConnectionA.LoadFrom(profile.ConnectionA);
        ConnectionB.LoadFrom(profile.ConnectionB);
    }

    private async Task SaveLastUsedProfileAsync()
    {
        var profile = new ComparisonProfile
        {
            Name = LastUsedProfileName,
            ConnectionA = ConnectionA.ToProfile(),
            ConnectionB = ConnectionB.ToProfile(),
        };

        await _profileStore.SaveAsync(profile);
    }

    private void NotifyCompareStateChanged()
    {
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(CompareStatusMessage));
    }

    [RelayCommand]
    private void CopySourceToTarget() => ConnectionB.CopyFieldsFrom(ConnectionA);

    [RelayCommand]
    private void CopyTargetToSource() => ConnectionA.CopyFieldsFrom(ConnectionB);

    [RelayCommand]
    private void SwapSourceAndTarget()
    {
        var sourceSnapshot = (
            ConnectionA.ServerName, ConnectionA.DatabaseName, ConnectionA.UserId,
            ConnectionA.Encrypt, ConnectionA.TrustServerCertificate, ConnectionA.RememberCredentials);

        ConnectionA.CopyFieldsFrom(ConnectionB);

        ConnectionB.ServerName = sourceSnapshot.ServerName;
        ConnectionB.DatabaseName = sourceSnapshot.DatabaseName;
        ConnectionB.UserId = sourceSnapshot.UserId;
        ConnectionB.Encrypt = sourceSnapshot.Encrypt;
        ConnectionB.TrustServerCertificate = sourceSnapshot.TrustServerCertificate;
        ConnectionB.RememberCredentials = sourceSnapshot.RememberCredentials;
    }

    private CancellationTokenSource? _compareCancellationTokenSource;

    /// <summary>Runs schema then data comparison and raises <see cref="ResultsReady"/>. Passwords come
    /// from the view's PasswordBoxes (not bindable via MVVM), so this is invoked from code-behind
    /// rather than as a RelayCommand — <see cref="CanCompare"/> is what gates the button's enabled state.</summary>
    public async Task CompareAsync(string sourcePassword, string targetPassword)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        _compareCancellationTokenSource = cancellationTokenSource;
        var cancellationToken = cancellationTokenSource.Token;

        IsComparing = true;
        StartElapsedTimer();
        try
        {
            ConnectionA.SaveCredentialIfRemembered(sourcePassword);
            ConnectionB.SaveCredentialIfRemembered(targetPassword);
            await SaveLastUsedProfileAsync();

            await RunSchemaComparisonAsync(sourcePassword, targetPassword, cancellationToken);

            // Data comparison can take a long time against a real database (full per-table scans);
            // it was cut out of this flow earlier because it looked hung with no feedback. Now that
            // CompareStatusMessage surfaces live per-table progress (see DataComparisonStatus below)
            // and IsComparing disables the button while running, it's back in — the Data tab's UI is
            // still the original plain tree, not the Schema tab's grid, and no exclusion rules exist
            // yet (deliberate — see planning.md §6a), so expect most rows to show as "changed" until
            // exclusions are built.
            await RunDataComparisonAsync(sourcePassword, targetPassword, cancellationToken);

            ResultsReady?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            SchemaComparisonStatus = "Cancelled.";
            DataComparisonStatus = "Cancelled.";
        }
        finally
        {
            IsComparing = false;
            _compareCancellationTokenSource = null;
            StopElapsedTimer();
        }
    }

    /// <summary>
    /// Starts the progress popup's running elapsed-time display — ticks once a second for as long as
    /// a comparison is in flight, so a long wait (e.g. a large table's chunks still running, per
    /// planning.md §19) shows how much time has actually passed instead of leaving that to be
    /// inferred from outside the app.
    /// </summary>
    /// <returns>returns nothing; this is a System.Void method</returns>
    private void StartElapsedTimer()
    {
        _comparisonStopwatch.Restart();
        ElapsedTimeText = FormatElapsedTime(TimeSpan.Zero);
        _elapsedTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimeTimer.Tick += (_, _) => ElapsedTimeText = FormatElapsedTime(_comparisonStopwatch.Elapsed);
        _elapsedTimeTimer.Start();
    }

    /// <summary>
    /// Stops the elapsed-time display, leaving the final value showing (success, failure, or
    /// cancellation) as the comparison's total elapsed time.
    /// </summary>
    /// <returns>returns nothing; this is a System.Void method</returns>
    private void StopElapsedTimer()
    {
        _elapsedTimeTimer?.Stop();
        _elapsedTimeTimer = null;
        _comparisonStopwatch.Stop();
    }

    /// <summary>
    /// Formats an elapsed duration for display, at whatever precision is meaningful for its
    /// magnitude — seconds alone under a minute, minutes and seconds under an hour, hours and minutes
    /// beyond that.
    /// </summary>
    /// <param name="elapsed">a System.TimeSpan holding the duration to format</param>
    /// <returns>returns a System.String such as "Elapsed: 4m 12s"</returns>
    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"Elapsed: {(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"Elapsed: {(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        return $"Elapsed: {elapsed.Seconds}s";
    }

    /// <summary>Requests cancellation of an in-progress <see cref="CompareAsync"/> run. Cooperative,
    /// not instant — SQL commands observe the token and send an attention signal to the server, so
    /// there's a short delay while the current query actually stops.</summary>
    public void CancelCompare() => _compareCancellationTokenSource?.Cancel();

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var profile = new ComparisonProfile
        {
            Name = ProfileName,
            ConnectionA = ConnectionA.ToProfile(),
            ConnectionB = ConnectionB.ToProfile(),
        };

        await _profileStore.SaveAsync(profile);
        SaveStatusMessage = $"Saved profile '{ProfileName}'.";
    }

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        var profile = await _profileStore.LoadAsync(ProfileName);
        if (profile is null)
        {
            SaveStatusMessage = $"No saved profile named '{ProfileName}'.";
            return;
        }

        ConnectionA.LoadFrom(profile.ConnectionA);
        ConnectionB.LoadFrom(profile.ConnectionB);
        SaveStatusMessage = $"Loaded profile '{ProfileName}'.";
    }

    private async Task RunSchemaComparisonAsync(string sourcePassword, string targetPassword, CancellationToken cancellationToken)
    {
        SchemaComparisonStatus = "Reading schema...";
        try
        {
            var sourceProfile = ConnectionA.ToProfile();
            var targetProfile = ConnectionB.ToProfile();

            await using var sourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword);
            await using var targetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword);
            await sourceConnection.OpenAsync(cancellationToken);
            await targetConnection.OpenAsync(cancellationToken);

            var sourceSchema = await _schemaReader.ReadSchemaAsync(sourceConnection, cancellationToken);
            var targetSchema = await _schemaReader.ReadSchemaAsync(targetConnection, cancellationToken);
            _lastSourceSchema = sourceSchema;
            _lastTargetSchema = targetSchema;

            var result = _schemaComparer.Compare(sourceSchema, targetSchema);
            _lastSchemaDiffResult = result;
            SchemaRowsView = BuildSchemaRowsView(result, sourceSchema, targetSchema);
            SelectedSchemaRow = null;
            SelectedSchemaObjectHeader = string.Empty;
            SourceDdlLines = [];
            TargetDdlLines = [];
            SchemaComparisonStatus = result.IsIdentical
                ? "Schemas are identical."
                : $"{result.TablesOnlyInSource.Count} table(s) only in source, " +
                  $"{result.TablesOnlyInTarget.Count} only in target, " +
                  $"{result.TableDiffs.Count} table(s) with column differences.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SchemaComparisonStatus = $"Failed: {ex.Message}";
        }
    }

    // SqlConnection can't run more than one command at a time, so parallelizing across tables means
    // each concurrent table needs its own connection pair, not a shared one. Capped rather than
    // unbounded so a database with hundreds of tables doesn't try to open hundreds of connections
    // at once — most SQL Server instances default to a few hundred max connections total anyway.
    private static readonly int DataComparisonMaxParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);

    private async Task RunDataComparisonAsync(string sourcePassword, string targetPassword, CancellationToken cancellationToken)
    {
        DataComparisonStatus = "Reading schema...";
        TableProgressItems = [];
        try
        {
            var sourceProfile = ConnectionA.ToProfile();
            var targetProfile = ConnectionB.ToProfile();
            _lastDataSourceProfile = sourceProfile;
            _lastDataTargetProfile = targetProfile;
            _lastDataSourcePassword = sourcePassword;
            _lastDataTargetPassword = targetPassword;

            DatabaseSchema sourceSchema;
            DatabaseSchema targetSchema;
            IReadOnlyDictionary<string, long> sourceRowCounts;
            IReadOnlyDictionary<string, long> targetRowCounts;
            await using (var schemaSourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword))
            await using (var schemaTargetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword))
            {
                await schemaSourceConnection.OpenAsync(cancellationToken);
                await schemaTargetConnection.OpenAsync(cancellationToken);
                sourceSchema = await _schemaReader.ReadSchemaAsync(schemaSourceConnection, cancellationToken);
                targetSchema = await _schemaReader.ReadSchemaAsync(schemaTargetConnection, cancellationToken);
                sourceRowCounts = await _rowCountReader.ReadApproximateRowCountsAsync(schemaSourceConnection, cancellationToken);
                targetRowCounts = await _rowCountReader.ReadApproximateRowCountsAsync(schemaTargetConnection, cancellationToken);
            }

            var targetTablesByName = targetSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var commonTables = sourceSchema.Tables
                .Where(t => targetTablesByName.ContainsKey(t.FullName))
                .Where(t => !TemporarilySkippedTablesForFasterIteration.Contains(t.FullName, StringComparer.OrdinalIgnoreCase))
                .Select(t => (Source: t, Target: targetTablesByName[t.FullName]))
                .ToList();

            if (commonTables.Count == 0)
            {
                DataComparisonStatus = "No tables exist on both sides.";
                DataDiffNodes = [];
                return;
            }

            var progressItems = new ObservableCollection<TableProgressItem>(
                commonTables.Select(t => new TableProgressItem(t.Source.FullName)));
            TableProgressItems = progressItems;

            var completedCount = 0;
            IProgress<int> progress = new Progress<int>(completed =>
                DataComparisonStatus = $"Compared {completed} of {commonTables.Count} table(s)...");
            IProgress<(int Index, DiffTreeNode? Node)> tableProgress = new Progress<(int Index, DiffTreeNode? Node)>(result =>
            {
                var item = progressItems[result.Index];
                item.IsComplete = true;
                item.HasDifferences = result.Node is not null;
                item.Summary = result.Node is null ? "No differences" : "Differences found";
            });

            // Marks one chunk of a partitioned large table complete and updates its parent row's
            // summary with a live "x/N complete" count — without this, a big table split across
            // several workers looks identical to a stuck one until every chunk finishes at once
            // (planning.md §19).
            IProgress<(ChunkProgressItem Chunk, TableProgressItem Table, int CompletedChunks, int TotalChunks)> chunkProgress =
                new Progress<(ChunkProgressItem Chunk, TableProgressItem Table, int CompletedChunks, int TotalChunks)>(result =>
                {
                    result.Chunk.IsComplete = true;
                    result.Table.Summary = $"Comparing {result.TotalChunks} chunks ({result.CompletedChunks}/{result.TotalChunks} complete)...";
                });

            var indexedResults = new ConcurrentBag<(int Index, DiffTreeNode Node)>();

            // Large tables get split into several key-range chunks instead of running as one long
            // single-threaded pass (planning.md §19) — but everything, small tables and large-table
            // chunks alike, is dispatched through ONE Parallel.ForEachAsync call below. An earlier
            // version ran small tables to completion first and only *then* started the large-table
            // chunks as a separate phase; that made the large-table split wait behind whichever small
            // table happened to be slowest, which defeated the point. Parallel.ForEachAsync already
            // pulls the next queued item into a freed slot the moment one finishes — no barrier
            // needed for large-table chunks to start alongside small tables from the very beginning.
            var partialResultsByIndex = new ConcurrentDictionary<int, ConcurrentBag<KeyedTableDiffResult>>();
            var remainingChunksByIndex = new ConcurrentDictionary<int, int>();
            var rowsByIndex = new ConcurrentDictionary<int, DataComparisonRow>();
            var jobs = new List<(TableSchema Source, TableSchema Target, int Index, KeyedComparisonPlan? Plan, KeyRange? Range, ChunkProgressItem? ChunkItem)>();

            foreach (var item in commonTables.Select((pair, index) => (pair.Source, pair.Target, Index: index)))
            {
                sourceRowCounts.TryGetValue(item.Source.FullName, out var sourceCount);
                targetRowCounts.TryGetValue(item.Target.FullName, out var targetCount);
                if (Math.Max(sourceCount, targetCount) < LargeTableRowCountThreshold)
                {
                    jobs.Add((item.Source, item.Target, item.Index, null, null, null));
                    continue;
                }

                var plan = DetermineKeyedComparisonPlan(item.Source, item.Target);
                if (plan.CommonColumns.Count == 0)
                {
                    tableProgress.Report((item.Index, null));
                    progress.Report(Interlocked.Increment(ref completedCount));
                    rowsByIndex[item.Index] = new DataComparisonRow(item.Source.FullName, 0, 0, 0, 0, 0, 0, null);
                    continue;
                }

                if (!plan.HasUsableKey)
                {
                    // No key to range-partition by — one whole-table job via the normal
                    // hash-fallback path, same as a small table.
                    jobs.Add((item.Source, item.Target, item.Index, null, null, null));
                    continue;
                }

                IReadOnlyList<object> boundaries;
                await using (var boundaryConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword))
                {
                    await boundaryConnection.OpenAsync(cancellationToken);
                    boundaries = await _rangePartitioner.ComputeBoundariesAsync(
                        boundaryConnection, item.Source, plan.KeyColumns[0], LargeTablePartitionCount, sourceCount, cancellationToken);
                }

                var ranges = TableRangePartitioner.BuildRanges(plan.KeyColumns[0], boundaries);
                remainingChunksByIndex[item.Index] = ranges.Count;

                var tableProgressItem = progressItems[item.Index];
                tableProgressItem.Summary = $"Comparing {ranges.Count} chunks (0/{ranges.Count} complete)...";
                for (var chunkNumber = 0; chunkNumber < ranges.Count; chunkNumber++)
                {
                    var range = ranges[chunkNumber];
                    var chunkItem = new ChunkProgressItem(FormatChunkLabel(chunkNumber + 1, ranges.Count, range));
                    tableProgressItem.Chunks.Add(chunkItem);
                    jobs.Add((item.Source, item.Target, item.Index, plan, range, chunkItem));
                }
            }

            await Parallel.ForEachAsync(
                jobs,
                new ParallelOptions { MaxDegreeOfParallelism = DataComparisonMaxParallelism, CancellationToken = cancellationToken },
                async (job, itemCancellationToken) =>
                {
                    await using var sourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword);
                    await using var targetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword);
                    await sourceConnection.OpenAsync(itemCancellationToken);
                    await targetConnection.OpenAsync(itemCancellationToken);

                    if (job.Range is null || job.Plan is null)
                    {
                        var outcome = await CompareOneTableAsync(sourceConnection, targetConnection, job.Source, job.Target, itemCancellationToken);
                        if (outcome.Node is not null)
                        {
                            indexedResults.Add((job.Index, outcome.Node));
                        }

                        rowsByIndex[job.Index] = outcome.Summary;
                        progress.Report(Interlocked.Increment(ref completedCount));
                        tableProgress.Report((job.Index, outcome.Node));
                        return;
                    }

                    var partialDiff = await _keyedTableComparer.CompareAsync(
                        sourceConnection, targetConnection, job.Source, job.Target,
                        job.Plan.KeyColumns, job.Plan.ValueColumns, MaxDiscrepanciesShownPerTable, job.Range, itemCancellationToken);

                    var bag = partialResultsByIndex.GetOrAdd(job.Index, _ => []);
                    bag.Add(partialDiff);

                    // remainingChunksByIndex is seeded with every chunked table's total chunk count
                    // before any of its chunk jobs run, so exactly one chunk per table observes the
                    // count reaching zero — that's the one responsible for combining and reporting.
                    var remaining = remainingChunksByIndex.AddOrUpdate(job.Index, 0, (_, current) => current - 1);
                    var totalChunks = bag.Count + remaining;
                    chunkProgress.Report((job.ChunkItem!, progressItems[job.Index], totalChunks - remaining, totalChunks));

                    if (remaining == 0)
                    {
                        var combined = KeyedTableDiffResult.Combine(job.Source.FullName, bag.ToList(), MaxDiscrepanciesShownPerTable);
                        var outcome = BuildKeyedOutcome(combined, job.Source, job.Target);
                        if (outcome.Node is not null)
                        {
                            indexedResults.Add((job.Index, outcome.Node));
                        }

                        rowsByIndex[job.Index] = outcome.Summary;
                        progress.Report(Interlocked.Increment(ref completedCount));
                        tableProgress.Report((job.Index, outcome.Node));
                    }
                });

            var rootNodes = new ObservableCollection<DiffTreeNode>(
                indexedResults.OrderBy(r => r.Index).Select(r => r.Node));

            DataDiffNodes = rootNodes;

            var dataComparisonRows = commonTables
                .Select((pair, index) => rowsByIndex.TryGetValue(index, out var row)
                    ? row
                    : new DataComparisonRow(pair.Source.FullName, 0, 0, 0, 0, 0, 0, null))
                .ToList();
            DataComparisonRowsView = BuildDataComparisonRowsView(dataComparisonRows);
            SelectedDataComparisonRow = null;
            _lastDataComparisonRows = dataComparisonRows;

            DataComparisonStatus = rootNodes.Count == 0
                ? $"Compared {commonTables.Count} table(s) — no data differences found."
                : $"Compared {commonTables.Count} table(s) — {rootNodes.Count} table(s) with data differences.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DataComparisonStatus = $"Failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds the display label for one key-range chunk of a partitioned large table (planning.md
    /// §19), e.g. "Chunk 2 of 5 (ID 10,720,068 – 21,440,134)".
    /// </summary>
    /// <param name="chunkNumber">this chunk's 1-based position among its table's chunks</param>
    /// <param name="totalChunks">the total number of chunks for this table</param>
    /// <param name="range">the DataCompare.Engine.DataComparison.KeyRange this chunk covers</param>
    /// <returns>returns a System.String label describing the chunk and the key range it covers</returns>
    private static string FormatChunkLabel(int chunkNumber, int totalChunks, KeyRange range)
    {
        var boundsText = (range.LowerExclusive, range.UpperInclusive) switch
        {
            (null, null) => "all rows",
            (null, var upper) => $"{range.ColumnName} ≤ {upper:N0}",
            (var lower, null) => $"{range.ColumnName} > {lower:N0}",
            (var lower, var upper) => $"{range.ColumnName} {lower:N0} – {upper:N0}",
        };

        return $"Chunk {chunkNumber} of {totalChunks} ({boundsText})";
    }

    /// <summary>Compares one table pair on its own connections. Returns null when the table is
    /// identical (or has no common columns) — nothing to add to the results tree.</summary>
    /// <summary>Result of comparing one table: the tree node for display (null when identical — see
    /// <see cref="DiffTreeNode"/>) plus a summary row that exists for every table regardless, so the
    /// Data comparison grid can show identical tables alongside differing ones.</summary>
    private readonly record struct TableComparisonOutcome(DiffTreeNode? Node, DataComparisonRow Summary);

    private async Task<TableComparisonOutcome> CompareOneTableAsync(
        SqlConnection sourceConnection, SqlConnection targetConnection, TableSchema sourceTable, TableSchema targetTable,
        CancellationToken cancellationToken)
    {
        var plan = DetermineKeyedComparisonPlan(sourceTable, targetTable);
        if (plan.CommonColumns.Count == 0)
        {
            return new TableComparisonOutcome(null, new DataComparisonRow(sourceTable.FullName, 0, 0, 0, 0, 0, 0, null));
        }

        if (plan.HasUsableKey)
        {
            // Primary path (planning.md §6, revised): streams both sides in key order and diffs in
            // lockstep, like SQL Data Compare — no per-row hashing, no full-table aggregation that
            // fails to collapse before exclusion rules exist.
            var keyedDiff = await _keyedTableComparer.CompareAsync(
                sourceConnection, targetConnection, sourceTable, targetTable,
                plan.KeyColumns, plan.ValueColumns, MaxDiscrepanciesShownPerTable, cancellationToken: cancellationToken);

            return BuildKeyedOutcome(keyedDiff, sourceTable, targetTable);
        }

        // Fallback for tables with no usable primary key — can't align rows by key, so fall back to
        // the slower full-row content hash multiset diff. The hash path can't distinguish a changed
        // row from an added-plus-removed pair without a key, so all discrepancy volume folds into
        // missing-from-target/missing-from-source rather than a separate "changed" count.
        var sourceCounts = await _tableHashComparer.ReadHashCountsAsync(sourceConnection, sourceTable, plan.CommonColumns, cancellationToken);
        var targetCounts = await _tableHashComparer.ReadHashCountsAsync(targetConnection, targetTable, plan.CommonColumns, cancellationToken);
        var diff = _tableHashComparer.Diff(sourceTable.FullName, sourceCounts, targetCounts);

        var node = diff.IsIdentical
            ? null
            : await BuildTableDiffNodeAsync(sourceConnection, targetConnection, sourceTable, targetTable, plan.CommonColumns, diff, cancellationToken);
        var summary = new DataComparisonRow(
            sourceTable.FullName, diff.SourceRowCount, diff.TargetRowCount, diff.MatchedRowCount,
            0, diff.RowsMissingFromTarget, diff.RowsMissingFromSource, node);
        return new TableComparisonOutcome(node, summary);
    }

    /// <summary>Builds the node+summary outcome for a keyed comparison result — shared between the
    /// normal per-table path and the large-table chunk-combine path (planning.md §19), since both
    /// end up with a <see cref="KeyedTableDiffResult"/> to report on.</summary>
    /// <param name="keyedDiff">a DataCompare.Engine.DataComparison.KeyedTableDiffResult to build the outcome from</param>
    /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
    /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
    /// <returns>returns a TableComparisonOutcome summarizing this table's comparison</returns>
    private TableComparisonOutcome BuildKeyedOutcome(KeyedTableDiffResult keyedDiff, TableSchema sourceTable, TableSchema targetTable)
    {
        var node = keyedDiff.IsIdentical ? null : BuildKeyedTableDiffNode(keyedDiff, sourceTable, targetTable);
        var summary = new DataComparisonRow(
            sourceTable.FullName, keyedDiff.SourceRowCount, keyedDiff.TargetRowCount, keyedDiff.MatchedIdenticalCount,
            keyedDiff.ChangedRows.TotalCount, keyedDiff.RowsOnlyInSource.TotalCount, keyedDiff.RowsOnlyInTarget.TotalCount, node);
        return new TableComparisonOutcome(node, summary);
    }

    /// <summary>
    /// Determines the common columns, primary-key columns, and whether both sides' keys line up
    /// well enough to use the fast keyed merge-join path — shared between the normal per-table
    /// comparison and the large-table range-partitioned comparison (planning.md §19), which both
    /// need to make the same keyed-vs-hash-fallback decision.
    /// </summary>
    /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
    /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
    /// <returns>returns a KeyedComparisonPlan describing the common, key, and value columns, and whether the keyed path is usable</returns>
    private static KeyedComparisonPlan DetermineKeyedComparisonPlan(TableSchema sourceTable, TableSchema targetTable)
    {
        var commonColumns = DetermineCommonColumns(sourceTable, targetTable);
        var sourceKeyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var targetKeyColumns = targetTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var hasUsableKey = sourceKeyColumns.Count > 0
            && sourceKeyColumns.SequenceEqual(targetKeyColumns, StringComparer.OrdinalIgnoreCase);
        var valueColumns = hasUsableKey
            ? commonColumns.Where(c => !sourceKeyColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
            : [];

        return new KeyedComparisonPlan(commonColumns, sourceKeyColumns, valueColumns, hasUsableKey);
    }

    /// <summary>Shared result of <see cref="DetermineKeyedComparisonPlan"/>.</summary>
    private sealed record KeyedComparisonPlan(
        List<string> CommonColumns, List<string> KeyColumns, List<string> ValueColumns, bool HasUsableKey);

    private async Task<DiffTreeNode> BuildTableDiffNodeAsync(
        SqlConnection sourceConnection,
        SqlConnection targetConnection,
        TableSchema sourceTable,
        TableSchema targetTable,
        List<string> commonColumns,
        TableDataDiffResult diff,
        CancellationToken cancellationToken)
    {
        var tableNode = new DiffTreeNode(
            $"{diff.TableName}: source={diff.SourceRowCount}, target={diff.TargetRowCount}, " +
            $"matched={diff.MatchedRowCount}, missing-from-target={diff.RowsMissingFromTarget}, " +
            $"missing-from-source={diff.RowsMissingFromSource}");

        var discrepanciesToShow = diff.Discrepancies.Take(MaxDiscrepanciesShownPerTable).ToList();
        foreach (var discrepancy in discrepanciesToShow)
        {
            var discrepancyNode = new DiffTreeNode(
                $"hash {discrepancy.Hash[..Math.Min(8, discrepancy.Hash.Length)]}...: " +
                $"source count={discrepancy.SourceCount}, target count={discrepancy.TargetCount}");

            var sampleFromSource = discrepancy.MissingFromTarget > 0;
            var sampleConnection = sampleFromSource ? sourceConnection : targetConnection;
            var sampleTable = sampleFromSource ? sourceTable : targetTable;

            var samples = await _drillDownFetcher.FetchSampleRowsAsync(
                sampleConnection, sampleTable, commonColumns, discrepancy.Hash, SampleRowsPerDiscrepancy, cancellationToken);

            foreach (var sample in samples)
            {
                var text = string.Join(", ", sample.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
                discrepancyNode.Children.Add(new DiffTreeNode(text));
            }

            tableNode.Children.Add(discrepancyNode);
        }

        if (diff.Discrepancies.Count > discrepanciesToShow.Count)
        {
            tableNode.Children.Add(new DiffTreeNode(
                $"... {diff.Discrepancies.Count - discrepanciesToShow.Count} more discrepancies not shown."));
        }

        return tableNode;
    }

    private DiffTreeNode BuildKeyedTableDiffNode(KeyedTableDiffResult diff, TableSchema sourceTable, TableSchema targetTable)
    {
        var tableNode = new DiffTreeNode(
            $"{diff.TableName}: source={diff.SourceRowCount}, target={diff.TargetRowCount}, " +
            $"matched={diff.MatchedIdenticalCount}, changed={diff.ChangedRows.TotalCount}, " +
            $"missing-from-target={diff.RowsOnlyInSource.TotalCount}, missing-from-source={diff.RowsOnlyInTarget.TotalCount}");

        if (diff.RowsOnlyInSource.TotalCount > 0)
        {
            var node = new DiffTreeNode($"Rows only in Source ({diff.RowsOnlyInSource.TotalCount})");
            AddRowExamples(node, diff.RowsOnlyInSource);
            tableNode.Children.Add(node);
        }

        if (diff.RowsOnlyInTarget.TotalCount > 0)
        {
            var node = new DiffTreeNode($"Rows only in Target ({diff.RowsOnlyInTarget.TotalCount})");
            AddRowExamples(node, diff.RowsOnlyInTarget);
            tableNode.Children.Add(node);
        }

        if (diff.ChangedRows.TotalCount > 0)
        {
            var node = new DiffTreeNode($"Rows with changed values ({diff.ChangedRows.TotalCount})");
            var sourceColumnsByName = sourceTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var changedRow in diff.ChangedRows.Examples)
            {
                var keyText = string.Join(", ", changedRow.KeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
                var changedRowNode = new DiffTreeNode($"[{keyText}] changed: {string.Join(", ", changedRow.ChangedColumnNames)}");
                foreach (var columnName in changedRow.ChangedColumnNames)
                {
                    changedRowNode.Children.Add(BuildChangedColumnNode(
                        sourceColumnsByName[columnName], columnName, changedRow, sourceTable, targetTable));
                }

                node.Children.Add(changedRowNode);
            }

            if (diff.ChangedRows.TotalCount > diff.ChangedRows.Examples.Count)
            {
                node.Children.Add(new DiffTreeNode($"... {diff.ChangedRows.TotalCount - diff.ChangedRows.Examples.Count} more not shown."));
            }

            tableNode.Children.Add(node);
        }

        return tableNode;
    }

    /// <summary>
    /// Builds the display node for one changed column within a changed row. MAX-length binary/text
    /// columns hold a hash, not the real value (see <see cref="LargeContentColumn"/>), so they're
    /// described rather than printed; varbinary columns additionally get an "Open both..." action
    /// that drills down to the real values on demand (planning.md §18).
    /// </summary>
    /// <param name="column">a DataCompare.Engine.Schema.ColumnSchema describing the changed column</param>
    /// <param name="columnName">the changed column's name</param>
    /// <param name="changedRow">a DataCompare.Engine.DataComparison.ChangedRowExample holding the row's key and both sides' values</param>
    /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
    /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
    /// <returns>returns a DataCompare.App.ViewModels.DiffTreeNode describing this column's change</returns>
    private DiffTreeNode BuildChangedColumnNode(
        ColumnSchema column, string columnName, ChangedRowExample changedRow, TableSchema sourceTable, TableSchema targetTable)
    {
        if (!LargeContentColumn.Is(column))
        {
            return new DiffTreeNode(
                $"{columnName}: source={FormatValue(changedRow.SourceValues[columnName])} -> target={FormatValue(changedRow.TargetValues[columnName])}");
        }

        if (!string.Equals(column.DataType, "varbinary", StringComparison.OrdinalIgnoreCase))
        {
            // Large text columns (nvarchar/varchar(max)) are hash-compared for the same reason as
            // varbinary(max), but "open in the OS default app" only makes sense for binary file
            // content — text values just get flagged as changed with no drill-down action for now.
            return new DiffTreeNode($"{columnName}: content differs (large text column — hash mismatch)");
        }

        return new DiffTreeNode($"{columnName}: content differs (large column — hash mismatch)")
        {
            ActionLabel = "Open both...",
            ActionCommand = new AsyncRelayCommand(() =>
                OpenLargeContentBothAsync(sourceTable, targetTable, columnName, changedRow.KeyValues)),
        };
    }

    /// <summary>
    /// Re-fetches one row's real large-content value from both sides — the compare pass keeps only
    /// a hash (see <see cref="LargeContentColumn"/>) — and opens each in the OS default application
    /// so the user can inspect the difference directly, since binary formats like PDF/XLSX aren't
    /// diffed automatically (planning.md §18).
    /// </summary>
    /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
    /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
    /// <param name="columnName">the varbinary column whose value differs for this row</param>
    /// <param name="keyValues">the row's primary-key column name/value pairs, used to re-locate it</param>
    /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous open operation</returns>
    private async Task OpenLargeContentBothAsync(
        TableSchema sourceTable, TableSchema targetTable, string columnName, IReadOnlyDictionary<string, object?> keyValues)
    {
        if (_lastDataSourceProfile is null || _lastDataTargetProfile is null
            || _lastDataSourcePassword is null || _lastDataTargetPassword is null)
        {
            DataComparisonStatus = "Run a comparison first — there's nothing to open yet.";
            return;
        }

        try
        {
            await using var sourceConnection = _connectionFactory.CreateConnection(_lastDataSourceProfile, _lastDataSourcePassword);
            await using var targetConnection = _connectionFactory.CreateConnection(_lastDataTargetProfile, _lastDataTargetPassword);
            await sourceConnection.OpenAsync();
            await targetConnection.OpenAsync();

            var sourceBytes = await _largeContentValueFetcher.FetchValueAsync(sourceConnection, sourceTable, columnName, keyValues);
            var targetBytes = await _largeContentValueFetcher.FetchValueAsync(targetConnection, targetTable, columnName, keyValues);

            if (sourceBytes is null || targetBytes is null)
            {
                DataComparisonStatus = "Could not re-fetch that row's content — it may have changed since the comparison ran.";
                return;
            }

            DataComparisonStatus = DescribeFrontPageAnomaly(keyValues, sourceBytes, targetBytes)
                ?? "Opened both files for comparison.";

            OpenBytesInDefaultApp(sourceBytes, "source");
            OpenBytesInDefaultApp(targetBytes, "target");
        }
        catch (Exception ex)
        {
            DataComparisonStatus = $"Failed to open row content: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks whether a row marked IsFrontPage actually contains PDF content — a cheap data-
    /// integrity cross-check now that both files' real bytes are already in hand for the "Open
    /// both" action (planning.md §18). Deliberately not run proactively across the whole table yet
    /// — see the same section for that possible future expansion.
    /// </summary>
    /// <param name="keyValues">the row's primary-key column name/value pairs, expected to include IsFrontPage for tables that use this convention</param>
    /// <param name="sourceBytes">a System.Byte array holding the source side's raw content</param>
    /// <param name="targetBytes">a System.Byte array holding the target side's raw content</param>
    /// <returns>returns a System.String warning message when IsFrontPage is true but either side's content doesn't look like a PDF, otherwise null</returns>
    private static string? DescribeFrontPageAnomaly(IReadOnlyDictionary<string, object?> keyValues, byte[] sourceBytes, byte[] targetBytes)
    {
        if (keyValues.TryGetValue("IsFrontPage", out var isFrontPage) && isFrontPage is true
            && (!FileSignatureSniffer.LooksLikePdf(sourceBytes) || !FileSignatureSniffer.LooksLikePdf(targetBytes)))
        {
            return "Opened both files — but this row is marked IsFrontPage and its content doesn't look like a PDF. Worth checking the source data.";
        }

        return null;
    }

    /// <summary>
    /// Writes the given bytes to a uniquely-named temp file with an extension guessed from the
    /// content's magic bytes, then opens it via the OS default application.
    /// </summary>
    /// <param name="bytes">a System.Byte array holding the file content to open</param>
    /// <param name="label">a short System.String distinguishing this side ("source"/"target") in the temp file name</param>
    /// <returns>returns nothing; this is a System.Void method</returns>
    private static void OpenBytesInDefaultApp(byte[] bytes, string label)
    {
        var extension = FileSignatureSniffer.DetectExtension(bytes);
        var path = Path.Combine(Path.GetTempPath(), $"DataCompare_{label}_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void AddRowExamples(DiffTreeNode node, CappedExamples<RowExample> examples)
    {
        foreach (var example in examples.Examples)
        {
            var text = string.Join(", ", example.Values.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
            node.Children.Add(new DiffTreeNode(text));
        }

        if (examples.TotalCount > examples.Examples.Count)
        {
            node.Children.Add(new DiffTreeNode($"... {examples.TotalCount - examples.Examples.Count} more not shown."));
        }
    }

    private static List<string> DetermineCommonColumns(TableSchema source, TableSchema target)
    {
        var targetColumnNames = new HashSet<string>(target.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        return source.Columns.Select(c => c.Name).Where(targetColumnNames.Contains).ToList();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString() ?? string.Empty,
    };

    private static ICollectionView BuildSchemaRowsView(
        SchemaDiffResult result, DatabaseSchema sourceSchema, DatabaseSchema targetSchema)
    {
        var sourceByName = sourceSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
        var targetByName = targetSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

        var rows = new List<SchemaObjectRow>();

        rows.AddRange(result.TablesOnlyInSource.Select(name =>
        {
            var table = sourceByName[name];
            return new SchemaObjectRow(
                SchemaObjectDiffKind.OnlyInSource, "Only in Source", name,
                table.SchemaName, table.TableName, table.ModifiedAt, null, null, null);
        }));

        rows.AddRange(result.TablesOnlyInTarget.Select(name =>
        {
            var table = targetByName[name];
            return new SchemaObjectRow(
                SchemaObjectDiffKind.OnlyInTarget, "Only in Target", name,
                null, null, null, table.SchemaName, table.TableName, table.ModifiedAt);
        }));

        rows.AddRange(result.TableDiffs.Select(diff =>
        {
            var sourceTable = sourceByName[diff.TableName];
            var targetTable = targetByName[diff.TableName];
            return new SchemaObjectRow(
                SchemaObjectDiffKind.Changed, "Different", diff.TableName,
                sourceTable.SchemaName, sourceTable.TableName, sourceTable.ModifiedAt,
                targetTable.SchemaName, targetTable.TableName, targetTable.ModifiedAt);
        }));

        var changedNames = new HashSet<string>(result.TableDiffs.Select(d => d.TableName), StringComparer.OrdinalIgnoreCase);
        rows.AddRange(sourceSchema.Tables
            .Where(t => targetByName.ContainsKey(t.FullName) && !changedNames.Contains(t.FullName))
            .Select(t =>
            {
                var targetTable = targetByName[t.FullName];
                return new SchemaObjectRow(
                    SchemaObjectDiffKind.Identical, "Identical", t.FullName,
                    t.SchemaName, t.TableName, t.ModifiedAt,
                    targetTable.SchemaName, targetTable.TableName, targetTable.ModifiedAt);
            }));

        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SchemaObjectRow.GroupLabel)));
        return view;
    }

    partial void OnSelectedSchemaRowChanged(SchemaObjectRow? value)
    {
        if (value is null)
        {
            SelectedSchemaObjectHeader = string.Empty;
            SourceDdlLines = [];
            TargetDdlLines = [];
            return;
        }

        var sourceTable = _lastSourceSchema?.Tables.FirstOrDefault(t => string.Equals(t.FullName, value.FullName, StringComparison.OrdinalIgnoreCase));
        var targetTable = _lastTargetSchema?.Tables.FirstOrDefault(t => string.Equals(t.FullName, value.FullName, StringComparison.OrdinalIgnoreCase));

        SelectedSchemaObjectHeader = value.Kind switch
        {
            SchemaObjectDiffKind.OnlyInSource => $"{value.FullName}  →  (does not exist in target)",
            SchemaObjectDiffKind.OnlyInTarget => $"(does not exist in source)  →  {value.FullName}",
            _ => value.FullName,
        };

        var (sourceLines, targetLines) = TableDdlDiffBuilder.BuildDiffLines(sourceTable, targetTable);
        SourceDdlLines = new ObservableCollection<DdlLine>(sourceLines.Select(ToAppDdlLine));
        TargetDdlLines = new ObservableCollection<DdlLine>(targetLines.Select(ToAppDdlLine));
    }

    private static DdlLine ToAppDdlLine(TableDdlDiffLine line) => new(line.Text, line.IsHighlighted, line.IsPresent);

    partial void OnSelectedDataComparisonRowChanged(DataComparisonRow? value) =>
        SelectedDataComparisonDetailNodes = value?.DetailNode is { } node ? [node] : [];

    /// <summary>
    /// Builds the grouped view backing the Data comparison grid — one row per common table
    /// (identical or not), grouped into "Tables with differences" and "Identical tables" so a clean
    /// run doesn't bury the tables that actually matter under the ones that don't.
    /// </summary>
    /// <param name="rows">every common table's comparison summary, in table order</param>
    /// <returns>returns a System.Windows.Data.ICollectionView grouped by DataComparisonRow.GroupLabel</returns>
    private static ICollectionView BuildDataComparisonRowsView(IReadOnlyList<DataComparisonRow> rows)
    {
        var view = CollectionViewSource.GetDefaultView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DataComparisonRow.GroupLabel)));
        view.SortDescriptions.Add(new SortDescription(nameof(DataComparisonRow.GroupLabel), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(DataComparisonRow.TableName), ListSortDirection.Ascending));
        return view;
    }

    /// <summary>Generates the HTML schema report for the most recently run comparison, or null if
    /// none has run yet.</summary>
    public string? GenerateSchemaHtmlReport()
    {
        if (_lastSchemaDiffResult is null || _lastSourceSchema is null || _lastTargetSchema is null)
        {
            return null;
        }

        var sourceLabel = $"{ConnectionA.ServerName} / {ConnectionA.DatabaseName}";
        var targetLabel = $"{ConnectionB.ServerName} / {ConnectionB.DatabaseName}";
        return SchemaHtmlReportWriter.Generate(sourceLabel, targetLabel, _lastSchemaDiffResult, _lastSourceSchema, _lastTargetSchema);
    }

    /// <summary>Generates the HTML data comparison report for the most recently run comparison, or
    /// null if none has run yet.</summary>
    public string? GenerateDataComparisonHtmlReport()
    {
        if (_lastDataComparisonRows is null)
        {
            return null;
        }

        var summaries = _lastDataComparisonRows.Select(r => new DataComparisonTableSummary(
            r.TableName, r.SourceRowCount, r.TargetRowCount, r.MatchedCount,
            r.ChangedCount, r.MissingFromTargetCount, r.MissingFromSourceCount)).ToList();
        return DataComparisonHtmlReportWriter.Generate(
            ConnectionA.ServerName, ConnectionA.DatabaseName, ConnectionB.ServerName, ConnectionB.DatabaseName, summaries);
    }
}
