using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
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
    private ObservableCollection<TableProgressItem> _tableProgressItems = [];

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
        }
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

            DatabaseSchema sourceSchema;
            DatabaseSchema targetSchema;
            await using (var schemaSourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword))
            await using (var schemaTargetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword))
            {
                await schemaSourceConnection.OpenAsync(cancellationToken);
                await schemaTargetConnection.OpenAsync(cancellationToken);
                sourceSchema = await _schemaReader.ReadSchemaAsync(schemaSourceConnection, cancellationToken);
                targetSchema = await _schemaReader.ReadSchemaAsync(schemaTargetConnection, cancellationToken);
            }

            var targetTablesByName = targetSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var commonTables = sourceSchema.Tables
                .Where(t => targetTablesByName.ContainsKey(t.FullName))
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
                item.Summary = result.Node is null ? "No differences" : "Differences found";
            });

            var indexedResults = new ConcurrentBag<(int Index, DiffTreeNode Node)>();

            await Parallel.ForEachAsync(
                commonTables.Select((pair, index) => (pair.Source, pair.Target, Index: index)),
                new ParallelOptions { MaxDegreeOfParallelism = DataComparisonMaxParallelism, CancellationToken = cancellationToken },
                async (item, itemCancellationToken) =>
                {
                    await using var sourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword);
                    await using var targetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword);
                    await sourceConnection.OpenAsync(itemCancellationToken);
                    await targetConnection.OpenAsync(itemCancellationToken);

                    var node = await CompareOneTableAsync(sourceConnection, targetConnection, item.Source, item.Target, itemCancellationToken);
                    if (node is not null)
                    {
                        indexedResults.Add((item.Index, node));
                    }

                    progress.Report(Interlocked.Increment(ref completedCount));
                    tableProgress.Report((item.Index, node));
                });

            var rootNodes = new ObservableCollection<DiffTreeNode>(
                indexedResults.OrderBy(r => r.Index).Select(r => r.Node));

            DataDiffNodes = rootNodes;
            DataComparisonStatus = rootNodes.Count == 0
                ? $"Compared {commonTables.Count} table(s) — no data differences found."
                : $"Compared {commonTables.Count} table(s) — {rootNodes.Count} table(s) with data differences.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DataComparisonStatus = $"Failed: {ex.Message}";
        }
    }

    /// <summary>Compares one table pair on its own connections. Returns null when the table is
    /// identical (or has no common columns) — nothing to add to the results tree.</summary>
    private async Task<DiffTreeNode?> CompareOneTableAsync(
        SqlConnection sourceConnection, SqlConnection targetConnection, TableSchema sourceTable, TableSchema targetTable,
        CancellationToken cancellationToken)
    {
        var commonColumns = DetermineCommonColumns(sourceTable, targetTable);
        if (commonColumns.Count == 0)
        {
            return null;
        }

        var sourceKeyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var targetKeyColumns = targetTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var hasUsableKey = sourceKeyColumns.Count > 0
            && sourceKeyColumns.SequenceEqual(targetKeyColumns, StringComparer.OrdinalIgnoreCase);

        if (hasUsableKey)
        {
            // Primary path (planning.md §6, revised): streams both sides in key order and diffs in
            // lockstep, like SQL Data Compare — no per-row hashing, no full-table aggregation that
            // fails to collapse before exclusion rules exist.
            var valueColumns = commonColumns
                .Where(c => !sourceKeyColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var keyedDiff = await _keyedTableComparer.CompareAsync(
                sourceConnection, targetConnection, sourceTable, targetTable,
                sourceKeyColumns, valueColumns, MaxDiscrepanciesShownPerTable, cancellationToken);

            return keyedDiff.IsIdentical ? null : BuildKeyedTableDiffNode(keyedDiff);
        }

        // Fallback for tables with no usable primary key — can't align rows by key, so fall back to
        // the slower full-row content hash multiset diff.
        var sourceCounts = await _tableHashComparer.ReadHashCountsAsync(sourceConnection, sourceTable, commonColumns, cancellationToken);
        var targetCounts = await _tableHashComparer.ReadHashCountsAsync(targetConnection, targetTable, commonColumns, cancellationToken);
        var diff = _tableHashComparer.Diff(sourceTable.FullName, sourceCounts, targetCounts);

        return diff.IsIdentical
            ? null
            : await BuildTableDiffNodeAsync(sourceConnection, targetConnection, sourceTable, targetTable, commonColumns, diff, cancellationToken);
    }

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

    private static DiffTreeNode BuildKeyedTableDiffNode(KeyedTableDiffResult diff)
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
            foreach (var changedRow in diff.ChangedRows.Examples)
            {
                var keyText = string.Join(", ", changedRow.KeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
                var changedRowNode = new DiffTreeNode($"[{keyText}] changed: {string.Join(", ", changedRow.ChangedColumnNames)}");
                foreach (var columnName in changedRow.ChangedColumnNames)
                {
                    changedRowNode.Children.Add(new DiffTreeNode(
                        $"{columnName}: source={FormatValue(changedRow.SourceValues[columnName])} -> target={FormatValue(changedRow.TargetValues[columnName])}"));
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
}
