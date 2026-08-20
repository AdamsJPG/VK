using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

namespace DataCompare.App.ViewModels
{

    public partial class MainWindowViewModel : ObservableObject
    {
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
        private readonly DataComparisonOrchestrator _dataComparisonOrchestrator = new();
        private readonly LargeContentValueFetcher _largeContentValueFetcher = new();

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
        private IReadOnlyList<DataComparisonTableSummary>? _lastDataComparisonRows;

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

        /// <summary>the Source side's connection setup ViewModel.</summary>
        public ConnectionSetupViewModel ConnectionA { get; }

        /// <summary>the Target side's connection setup ViewModel.</summary>
        public ConnectionSetupViewModel ConnectionB { get; }

        /// <summary>Raised once both schema and data comparison finish, so the view can show the results window.</summary>
        public event EventHandler? ResultsReady;

        /// <summary>true when neither a comparison is running nor either side is missing its required fields.</summary>
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

        /// <summary>
        /// constructs the ViewModel with the real Windows Credential Manager and file-based profile
        /// store — the constructor WPF's designer/runtime actually uses.
        /// </summary>
        public MainWindowViewModel()
            : this(new WindowsCredentialStore(), new ComparisonProfileStore())
        {
        }

        /// <summary>
        /// constructs the ViewModel with injectable credential and profile stores, and wires up the
        /// Source/Target connection sub-ViewModels and their change notifications.
        /// </summary>
        /// <param name="credentialStore">a DataCompare.Engine.Security.ICredentialStore implementation used by both connection sides to save and look up remembered passwords</param>
        /// <param name="profileStore">a DataCompare.Engine.Profiles.ComparisonProfileStore used to save/load named and last-used comparison profiles</param>
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

        /// <summary>
        /// saves the current source/target connection field values as the implicit "last used" profile.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous save operation</returns>
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

        /// <summary>
        /// raises property-changed notifications for the two computed properties that depend on the
        /// connection fields' current state.
        /// </summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
        private void NotifyCompareStateChanged()
        {
            OnPropertyChanged(nameof(CanCompare));
            OnPropertyChanged(nameof(CompareStatusMessage));
        }

        /// <summary>copies the source side's field values onto the target side.</summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
        [RelayCommand]
        private void CopySourceToTarget() => ConnectionB.CopyFieldsFrom(ConnectionA);

        /// <summary>copies the target side's field values onto the source side.</summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
        [RelayCommand]
        private void CopyTargetToSource() => ConnectionA.CopyFieldsFrom(ConnectionB);

        /// <summary>
        /// swaps the source and target sides' field values with each other.
        /// </summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
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
            TableProgressItems = [];
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

        /// <summary>
        /// saves the current source/target connection field values under <see cref="ProfileName"/>.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous save operation</returns>
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

        /// <summary>
        /// loads the connection field values previously saved under <see cref="ProfileName"/>, if one exists.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous load operation</returns>
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

        /// <summary>
        /// connects to both sides, reads their schemas, compares them, and populates the Schema tab's
        /// grouped grid and status message.
        /// </summary>
        /// <param name="sourcePassword">a System.String holding the source connection's password</param>
        /// <param name="targetPassword">a System.String holding the target connection's password</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the comparison</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous schema comparison</returns>
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

                foreach (var tableName in result.TablesOnlyInSource)
                {
                    TableProgressItems.Add(new TableProgressItem(tableName)
                    {
                        IsSchemaOnly = true,
                        IsComplete = true,
                        Summary = "No matching table in Target",
                    });
                }

                foreach (var tableName in result.TablesOnlyInTarget)
                {
                    TableProgressItems.Add(new TableProgressItem(tableName)
                    {
                        IsSchemaOnly = true,
                        IsComplete = true,
                        Summary = "No matching table in Source",
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SchemaComparisonStatus = $"Failed: {ex.Message}";
            }
        }

        /// <summary>
        /// connects to both sides, reads their schemas and approximate row counts, then compares every
        /// common table's data via the shared <see cref="DataComparisonOrchestrator"/> (planning.md
        /// §19), and populates both the tree-based <see cref="DataDiffNodes"/> and the grid-based
        /// <see cref="DataComparisonRowsView"/>.
        /// </summary>
        /// <param name="sourcePassword">a System.String holding the source connection's password</param>
        /// <param name="targetPassword">a System.String holding the target connection's password</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the comparison</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous data comparison</returns>
        private async Task RunDataComparisonAsync(string sourcePassword, string targetPassword, CancellationToken cancellationToken)
        {
            DataComparisonStatus = "Reading schema...";
            // Schema-only orphan rows (planning.md §19 addendum) already sit in TableProgressItems by
            // the time this runs, added by RunSchemaComparisonAsync — so the orchestrator's own
            // TableIndex (0-based over just the tables it compares) no longer lines up with a plain
            // index into that shared collection. dataTableItems keeps the orchestrator's index space
            // separate while still appending each new row onto the same UI-bound collection.
            var dataTableItems = new List<TableProgressItem>();
            try
            {
                var sourceProfile = ConnectionA.ToProfile();
                var targetProfile = ConnectionB.ToProfile();
                _lastDataSourceProfile = sourceProfile;
                _lastDataTargetProfile = targetProfile;
                _lastDataSourcePassword = sourcePassword;
                _lastDataTargetPassword = targetPassword;

                var planProgress = new Progress<IReadOnlyList<DataComparisonTablePlan>>(plans =>
                {
                    foreach (var plan in plans)
                    {
                        var item = new TableProgressItem(plan.TableName);
                        dataTableItems.Add(item);
                        TableProgressItems.Add(item);
                    }
                });

                var tableChunkPlanProgress = new Progress<(int TableIndex, IReadOnlyList<DataComparisonChunkPlan> Chunks)>(result =>
                {
                    var item = dataTableItems[result.TableIndex];
                    item.Summary = $"Comparing {result.Chunks.Count} chunks (0/{result.Chunks.Count} complete)...";
                    foreach (var chunk in result.Chunks)
                    {
                        item.Chunks.Add(new ChunkProgressItem(chunk.Label));
                    }
                });

                var chunkProgress = new Progress<DataComparisonChunkProgress>(result =>
                {
                    var table = dataTableItems[result.TableIndex];
                    table.Chunks[result.ChunkIndex].IsComplete = true;
                    table.Summary = $"Comparing {result.TotalChunksForTable} chunks ({result.CompletedChunksForTable}/{result.TotalChunksForTable} complete)...";
                });

                var tableProgress = new Progress<DataComparisonTableProgress>(result =>
                {
                    var item = dataTableItems[result.TableIndex];
                    item.IsComplete = true;
                    item.HasDifferences = result.HasDifferences;
                    item.Summary = result.HasDifferences ? "Differences found" : "No differences";
                    DataComparisonStatus = $"Compared {result.CompletedTableCount} of {result.TotalTableCount} table(s)...";
                });

                var orchestrationResult = await _dataComparisonOrchestrator.RunAsync(
                    sourceProfile, sourcePassword, targetProfile, targetPassword,
                    TemporarilySkippedTablesForFasterIteration,
                    planProgress, tableChunkPlanProgress, chunkProgress, tableProgress, cancellationToken);

                if (orchestrationResult.ComparedTableCount == 0)
                {
                    DataComparisonStatus = "No tables exist on both sides.";
                    DataDiffNodes = [];
                    return;
                }

                var appRows = orchestrationResult.Rows.Select(ToAppRow).ToList();
                DataDiffNodes = new ObservableCollection<DiffTreeNode>(
                    appRows.Where(r => r.DetailNode is not null).Select(r => r.DetailNode!));
                DataComparisonRowsView = BuildDataComparisonRowsView(appRows);
                SelectedDataComparisonRow = null;
                _lastDataComparisonRows = orchestrationResult.Rows;

                DataComparisonStatus = orchestrationResult.DifferingTableCount == 0
                    ? $"Compared {orchestrationResult.ComparedTableCount} table(s) — no data differences found."
                    : $"Compared {orchestrationResult.ComparedTableCount} table(s) — " +
                      $"{orchestrationResult.DifferingTableCount} table(s) with data differences.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DataComparisonStatus = $"Failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Converts one orchestrator result row into the App-layer row the Data comparison grid binds
        /// to, rebuilding the on-demand "open both" click action (see <see
        /// cref="DataComparisonLargeContentAction"/>) that the plain Engine-layer detail tree can't
        /// carry itself.
        /// </summary>
        /// <param name="summary">a DataCompare.Engine.Reporting.DataComparisonTableSummary holding one table's comparison result</param>
        /// <returns>returns a DataCompare.App.ViewModels.DataComparisonRow bound by the Data comparison grid</returns>
        private DataComparisonRow ToAppRow(DataComparisonTableSummary summary) => new(
            summary.TableName, summary.SourceRowCount, summary.TargetRowCount, summary.MatchedCount,
            summary.ChangedCount, summary.MissingFromTargetCount, summary.MissingFromSourceCount,
            summary.ReassignedKeyCount, summary.Detail is null ? null : ToAppDiffTreeNode(summary.Detail));

        /// <summary>
        /// Converts one Engine-layer detail node (and all its descendants) into the App-layer tree node
        /// the on-screen TreeViews bind to, wiring up the "Open both..." click action for any node that
        /// carries a <see cref="DataComparisonLargeContentAction"/>.
        /// </summary>
        /// <param name="node">a DataCompare.Engine.Reporting.DataComparisonDetailNode to convert, including all descendants</param>
        /// <returns>returns a DataCompare.App.ViewModels.DiffTreeNode holding the same text, children, any drill-down action, and any comparison grid</returns>
        private DiffTreeNode ToAppDiffTreeNode(DataComparisonDetailNode node)
        {
            var treeNode = new DiffTreeNode(node.Text)
            {
                ActionLabel = node.LargeContentAction is null ? null : "Open both...",
                ActionCommand = node.LargeContentAction is { } action
                    ? new AsyncRelayCommand(() => OpenLargeContentBothAsync(action.SourceTable, action.TargetTable, action.ColumnName, action.KeyValues))
                    : null,
            };

            foreach (var child in node.Children)
            {
                treeNode.Children.Add(ToAppDiffTreeNode(child));
            }

            foreach (var column in node.GridColumns)
            {
                treeNode.SourceGridColumns.Add(new GridColumnCell(column.ColumnName, column.SourceValueDisplay, column.CellKind));
                treeNode.TargetGridColumns.Add(new GridColumnCell(column.ColumnName, column.TargetValueDisplay, column.CellKind));
            }

            return treeNode;
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

        /// <summary>
        /// Builds the grouped view backing the Schema tab's grid — one row per table across all four
        /// categories (only in source, only in target, different, identical).
        /// </summary>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.Windows.Data.ICollectionView grouped by SchemaObjectRow.GroupLabel</returns>
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

        /// <summary>
        /// updates the Schema tab's DDL detail pane to show the newly selected row's side-by-side diff,
        /// or clears it when the selection is cleared.
        /// </summary>
        /// <param name="value">a nullable DataCompare.App.ViewModels.SchemaObjectRow holding the newly selected row, or null when the selection is cleared</param>
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

        /// <summary>
        /// Converts an engine-layer DDL diff line into the App-layer view model type bound by the DDL panes.
        /// </summary>
        /// <param name="line">a DataCompare.Engine.Schema.TableDdlDiffLine to convert</param>
        /// <returns>returns a DataCompare.App.ViewModels.DdlLine holding the same text, highlight, and presence data</returns>
        private static DdlLine ToAppDdlLine(TableDdlDiffLine line) => new(line.Text, line.IsHighlighted, line.IsPresent);

        /// <summary>
        /// updates the Data comparison tab's detail pane to show the newly selected row's full diff
        /// detail, or clears it for an identical table (which has no detail to show) or no selection.
        /// </summary>
        /// <param name="value">a nullable DataCompare.App.ViewModels.DataComparisonRow holding the newly selected row, or null when the selection is cleared</param>
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

            return DataComparisonHtmlReportWriter.Generate(
                ConnectionA.ServerName, ConnectionA.DatabaseName, ConnectionB.ServerName, ConnectionB.DatabaseName, _lastDataComparisonRows);
        }
    }
}
