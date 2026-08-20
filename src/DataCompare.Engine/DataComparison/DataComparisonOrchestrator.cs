using System.Collections.Concurrent;
using DataCompare.Engine.Connections;
using DataCompare.Engine.Models;
using DataCompare.Engine.Reporting;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Runs a full data comparison across every table common to both sides of a connection pair —
    /// schema/row-count discovery, the keyed-merge-join-vs-hash-multiset decision per table, and
    /// range-partitioned dispatch for very large tables (planning.md §19) — behind a single, UI-free
    /// entry point so both the WPF app's Data comparison tab and the command-line mode can drive the
    /// exact same comparison logic instead of maintaining two copies of it.
    /// </summary>
    public sealed class DataComparisonOrchestrator
    {
        // No exclusion rules exist yet (that's phase 4) — data comparison compares every common column
        // on purpose, so examples reveal which columns are noise and belong in the exclusion list.
        private const int MaxDiscrepanciesShownPerTable = 10;
        private const int SampleRowsPerDiscrepancy = 3;

        // Tables at or above this row count are range-partitioned across several concurrent workers
        // instead of running as one long single-threaded pass (planning.md §19) — otherwise a handful
        // of huge tables each occupy one parallelism slot for their entire duration while everything
        // else finishes and slots sit idle. Their chunk jobs run in the SAME Parallel.ForEachAsync call
        // as every small table, not a separate later phase — an earlier version deferred them to a
        // second phase that only started once every small table finished, which made a large table's
        // split wait behind whichever small table happened to be slowest, defeating the point.
        private const long LargeTableRowCountThreshold = 1_000_000;
        private const int LargeTablePartitionCount = 5;

        // SqlConnection can't run more than one command at a time, so parallelizing across tables means
        // each concurrent table needs its own connection pair, not a shared one. Capped rather than
        // unbounded so a database with hundreds of tables doesn't try to open hundreds of connections
        // at once — most SQL Server instances default to a few hundred max connections total anyway.
        private static readonly int MaxParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);

        private readonly SqlConnectionFactory _connectionFactory = new();
        private readonly SchemaReader _schemaReader = new();
        private readonly TableRowCountReader _rowCountReader = new();
        private readonly TableRangePartitioner _rangePartitioner = new();
        private readonly KeyedTableComparer _keyedTableComparer = new();
        private readonly TableHashComparer _tableHashComparer = new();
        private readonly RowDrillDownFetcher _drillDownFetcher = new();

        /// <summary>
        /// connects to both sides, reads their schemas and approximate row counts, then compares every
        /// common table's data. Small tables and large-table key-range chunks are dispatched through
        /// one flat parallel job queue.
        /// </summary>
        /// <param name="sourceProfile">a DataCompare.Engine.Models.ConnectionProfile describing the source side of the comparison</param>
        /// <param name="sourcePassword">a System.String holding the source connection's password</param>
        /// <param name="targetProfile">a DataCompare.Engine.Models.ConnectionProfile describing the target side of the comparison</param>
        /// <param name="targetPassword">a System.String holding the target connection's password</param>
        /// <param name="excludedTableNames">a System.Collections.Generic.IReadOnlyCollection of System.String holding fully-qualified table names to skip regardless of existing on both sides</param>
        /// <param name="planProgress">an optional System.IProgress of System.Collections.Generic.IReadOnlyList of DataCompare.Engine.DataComparison.DataComparisonTablePlan, reported once with every compared table before any comparison work begins</param>
        /// <param name="tableChunkPlanProgress">an optional System.IProgress reporting, for one large table at a time as its key-range boundaries are computed, its table index and the resulting chunk plans</param>
        /// <param name="chunkProgress">an optional System.IProgress of DataCompare.Engine.DataComparison.DataComparisonChunkProgress, reported each time one key-range chunk of a partitioned large table finishes</param>
        /// <param name="tableProgress">an optional System.IProgress of DataCompare.Engine.DataComparison.DataComparisonTableProgress, reported each time one table's comparison finishes</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the comparison</param>
        /// <returns>returns a System.Threading.Tasks.Task of DataCompare.Engine.DataComparison.DataComparisonOrchestrationResult summarizing every compared table</returns>
        public async Task<DataComparisonOrchestrationResult> RunAsync(
            ConnectionProfile sourceProfile,
            string sourcePassword,
            ConnectionProfile targetProfile,
            string targetPassword,
            IReadOnlyCollection<string> excludedTableNames,
            IProgress<IReadOnlyList<DataComparisonTablePlan>>? planProgress,
            IProgress<(int TableIndex, IReadOnlyList<DataComparisonChunkPlan> Chunks)>? tableChunkPlanProgress,
            IProgress<DataComparisonChunkProgress>? chunkProgress,
            IProgress<DataComparisonTableProgress>? tableProgress,
            CancellationToken cancellationToken)
        {
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
                .Where(t => !excludedTableNames.Contains(t.FullName, StringComparer.OrdinalIgnoreCase))
                .Select(t => (Source: t, Target: targetTablesByName[t.FullName]))
                .ToList();

            if (commonTables.Count == 0)
            {
                return new DataComparisonOrchestrationResult([], 0, 0);
            }

            planProgress?.Report(commonTables.Select(t => new DataComparisonTablePlan(t.Source.FullName, [])).ToList());

            var completedCount = 0;
            var rowsByIndex = new ConcurrentDictionary<int, DataComparisonTableSummary>();
            var partialResultsByIndex = new ConcurrentDictionary<int, ConcurrentBag<KeyedTableDiffResult>>();
            var remainingChunksByIndex = new ConcurrentDictionary<int, int>();
            var jobs = new List<(TableSchema Source, TableSchema Target, int Index, KeyedComparisonPlan? Plan, KeyRange? Range, int ChunkIndex)>();

            foreach (var item in commonTables.Select((pair, index) => (pair.Source, pair.Target, Index: index)))
            {
                sourceRowCounts.TryGetValue(item.Source.FullName, out var sourceCount);
                targetRowCounts.TryGetValue(item.Target.FullName, out var targetCount);
                if (Math.Max(sourceCount, targetCount) < LargeTableRowCountThreshold)
                {
                    jobs.Add((item.Source, item.Target, item.Index, null, null, 0));
                    continue;
                }

                var plan = DetermineKeyedComparisonPlan(item.Source, item.Target);
                if (plan.CommonColumns.Count == 0)
                {
                    rowsByIndex[item.Index] = new DataComparisonTableSummary(item.Source.FullName, 0, 0, 0, 0, 0, 0);
                    tableProgress?.Report(new DataComparisonTableProgress(
                        item.Index, false, Interlocked.Increment(ref completedCount), commonTables.Count));
                    continue;
                }

                if (!plan.HasUsableKey)
                {
                    // No key to range-partition by — one whole-table job via the normal
                    // hash-fallback path, same as a small table.
                    jobs.Add((item.Source, item.Target, item.Index, null, null, 0));
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
                tableChunkPlanProgress?.Report((item.Index, ranges
                    .Select((range, chunkIndex) => new DataComparisonChunkPlan(FormatChunkLabel(chunkIndex + 1, ranges.Count, range)))
                    .ToList()));

                for (var chunkIndex = 0; chunkIndex < ranges.Count; chunkIndex++)
                {
                    jobs.Add((item.Source, item.Target, item.Index, plan, ranges[chunkIndex], chunkIndex));
                }
            }

            await Parallel.ForEachAsync(
                jobs,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
                async (job, itemCancellationToken) =>
                {
                    await using var sourceConnection = _connectionFactory.CreateConnection(sourceProfile, sourcePassword);
                    await using var targetConnection = _connectionFactory.CreateConnection(targetProfile, targetPassword);
                    await sourceConnection.OpenAsync(itemCancellationToken);
                    await targetConnection.OpenAsync(itemCancellationToken);

                    if (job.Range is null || job.Plan is null)
                    {
                        var summary = await CompareOneTableAsync(sourceConnection, targetConnection, job.Source, job.Target, itemCancellationToken);
                        rowsByIndex[job.Index] = summary;
                        tableProgress?.Report(new DataComparisonTableProgress(
                            job.Index, summary.HasDifferences, Interlocked.Increment(ref completedCount), commonTables.Count));
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
                    var totalChunksForTable = bag.Count + remaining;
                    chunkProgress?.Report(new DataComparisonChunkProgress(
                        job.Index, job.ChunkIndex, totalChunksForTable - remaining, totalChunksForTable));

                    if (remaining == 0)
                    {
                        var combined = KeyedTableDiffResult.Combine(job.Source.FullName, bag.ToList(), MaxDiscrepanciesShownPerTable);
                        var summary = BuildKeyedSummary(combined, job.Source, job.Target, job.Plan.KeyColumns, job.Plan.ValueColumns);
                        rowsByIndex[job.Index] = summary;
                        tableProgress?.Report(new DataComparisonTableProgress(
                            job.Index, summary.HasDifferences, Interlocked.Increment(ref completedCount), commonTables.Count));
                    }
                });

            var rows = commonTables
                .Select((pair, index) => rowsByIndex.TryGetValue(index, out var row)
                    ? row
                    : new DataComparisonTableSummary(pair.Source.FullName, 0, 0, 0, 0, 0, 0))
                .ToList();

            return new DataComparisonOrchestrationResult(rows, commonTables.Count, rows.Count(r => r.HasDifferences));
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

        /// <summary>
        /// Compares one table pair on its own connections. The returned summary's <see
        /// cref="DataComparisonTableSummary.Detail"/> is null when the table is identical (or has no
        /// common columns) — every table gets a summary row regardless, so the caller can show
        /// identical tables alongside differing ones.
        /// </summary>
        /// <param name="sourceConnection">an open Microsoft.Data.SqlClient.SqlConnection to the source database</param>
        /// <param name="targetConnection">an open Microsoft.Data.SqlClient.SqlConnection to the target database</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the comparison</param>
        /// <returns>returns a System.Threading.Tasks.Task of DataCompare.Engine.Reporting.DataComparisonTableSummary describing this table's comparison</returns>
        private async Task<DataComparisonTableSummary> CompareOneTableAsync(
            SqlConnection sourceConnection, SqlConnection targetConnection, TableSchema sourceTable, TableSchema targetTable,
            CancellationToken cancellationToken)
        {
            var plan = DetermineKeyedComparisonPlan(sourceTable, targetTable);
            if (plan.CommonColumns.Count == 0)
            {
                return new DataComparisonTableSummary(sourceTable.FullName, 0, 0, 0, 0, 0, 0);
            }

            if (plan.HasUsableKey)
            {
                // Primary path (planning.md §6, revised): streams both sides in key order and diffs in
                // lockstep, like SQL Data Compare — no per-row hashing, no full-table aggregation that
                // fails to collapse before exclusion rules exist.
                var keyedDiff = await _keyedTableComparer.CompareAsync(
                    sourceConnection, targetConnection, sourceTable, targetTable,
                    plan.KeyColumns, plan.ValueColumns, MaxDiscrepanciesShownPerTable, cancellationToken: cancellationToken);

                return BuildKeyedSummary(keyedDiff, sourceTable, targetTable, plan.KeyColumns, plan.ValueColumns);
            }

            // Fallback for tables with no usable primary key — can't align rows by key, so fall back to
            // the slower full-row content hash multiset diff. The hash path can't distinguish a changed
            // row from an added-plus-removed pair without a key, so all discrepancy volume folds into
            // missing-from-target/missing-from-source rather than a separate "changed" count.
            var sourceCounts = await _tableHashComparer.ReadHashCountsAsync(sourceConnection, sourceTable, plan.CommonColumns, cancellationToken);
            var targetCounts = await _tableHashComparer.ReadHashCountsAsync(targetConnection, targetTable, plan.CommonColumns, cancellationToken);
            var diff = _tableHashComparer.Diff(sourceTable.FullName, sourceCounts, targetCounts);

            var detail = diff.IsIdentical
                ? null
                : await BuildTableDiffNodeAsync(sourceConnection, targetConnection, sourceTable, targetTable, plan.CommonColumns, diff, cancellationToken);

            return new DataComparisonTableSummary(
                sourceTable.FullName, diff.SourceRowCount, diff.TargetRowCount, diff.MatchedRowCount,
                0, diff.RowsMissingFromTarget, diff.RowsMissingFromSource, Detail: detail);
        }

        /// <summary>Builds the summary for a keyed comparison result — shared between the normal
        /// per-table path and the large-table chunk-combine path (planning.md §19), since both end up
        /// with a <see cref="KeyedTableDiffResult"/> to summarize. Reconciles reassigned-key rows (rows
        /// found on both sides with identical non-key values but a different key) before summarizing,
        /// since this is the one point both call sites converge on with the full-table view already
        /// assembled.</summary>
        /// <param name="keyedDiff">a DataCompare.Engine.DataComparison.KeyedTableDiffResult to build the summary from</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <param name="keyColumnNames">a System.Collections.Generic.List of System.String holding the primary-key column names used for this comparison</param>
        /// <param name="valueColumnNames">a System.Collections.Generic.List of System.String holding the non-key column names used for this comparison</param>
        /// <returns>returns a DataCompare.Engine.Reporting.DataComparisonTableSummary summarizing this table's comparison</returns>
        private DataComparisonTableSummary BuildKeyedSummary(
            KeyedTableDiffResult keyedDiff, TableSchema sourceTable, TableSchema targetTable,
            List<string> keyColumnNames, List<string> valueColumnNames)
        {
            keyedDiff = keyedDiff.ReconcileReassignedKeys(keyColumnNames, valueColumnNames, MaxDiscrepanciesShownPerTable);
            var detail = keyedDiff.IsIdentical ? null : BuildKeyedTableDiffNode(keyedDiff, sourceTable, targetTable);
            return new DataComparisonTableSummary(
                sourceTable.FullName, keyedDiff.SourceRowCount, keyedDiff.TargetRowCount, keyedDiff.MatchedIdenticalCount,
                keyedDiff.ChangedRows.TotalCount, keyedDiff.RowsOnlyInSource.TotalCount, keyedDiff.RowsOnlyInTarget.TotalCount,
                keyedDiff.ReassignedKeyRows.TotalCount, detail);
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
        /// <param name="CommonColumns">a System.Collections.Generic.List of System.String holding the column names present on both sides</param>
        /// <param name="KeyColumns">a System.Collections.Generic.List of System.String holding the source side's primary-key column names, in key order</param>
        /// <param name="ValueColumns">a System.Collections.Generic.List of System.String holding the common non-key column names, empty when no usable key exists</param>
        /// <param name="HasUsableKey">a System.Boolean that is true when both sides share the same primary-key column names in the same order</param>
        private sealed record KeyedComparisonPlan(
            List<string> CommonColumns, List<string> KeyColumns, List<string> ValueColumns, bool HasUsableKey);

        /// <summary>
        /// Determines which columns exist, by name, on both sides of a table (case-insensitive).
        /// </summary>
        /// <param name="source">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="target">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <returns>returns a System.Collections.Generic.List of System.String containing the column names present on both sides</returns>
        private static List<string> DetermineCommonColumns(TableSchema source, TableSchema target)
        {
            var targetColumnNames = new HashSet<string>(target.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            return source.Columns.Select(c => c.Name).Where(targetColumnNames.Contains).ToList();
        }

        /// <summary>
        /// Builds the row-level detail tree for a hash-fallback table comparison (planning.md §18):
        /// one node per content-hash discrepancy, each with sample rows fetched from whichever side
        /// has the surplus.
        /// </summary>
        /// <param name="sourceConnection">an open Microsoft.Data.SqlClient.SqlConnection to the source database</param>
        /// <param name="targetConnection">an open Microsoft.Data.SqlClient.SqlConnection to the target database</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <param name="commonColumns">a System.Collections.Generic.List of System.String holding the column names present on both sides</param>
        /// <param name="diff">a DataCompare.Engine.DataComparison.TableDataDiffResult holding the hash comparison to render</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the sample fetches</param>
        /// <returns>returns a System.Threading.Tasks.Task of DataCompare.Engine.Reporting.DataComparisonDetailNode summarizing this table's differences</returns>
        private async Task<DataComparisonDetailNode> BuildTableDiffNodeAsync(
            SqlConnection sourceConnection,
            SqlConnection targetConnection,
            TableSchema sourceTable,
            TableSchema targetTable,
            List<string> commonColumns,
            TableDataDiffResult diff,
            CancellationToken cancellationToken)
        {
            var discrepanciesToShow = diff.Discrepancies.Take(MaxDiscrepanciesShownPerTable).ToList();
            var discrepancyNodes = new List<DataComparisonDetailNode>();
            foreach (var discrepancy in discrepanciesToShow)
            {
                var sampleFromSource = discrepancy.MissingFromTarget > 0;
                var sampleConnection = sampleFromSource ? sourceConnection : targetConnection;
                var sampleTable = sampleFromSource ? sourceTable : targetTable;

                var samples = await _drillDownFetcher.FetchSampleRowsAsync(
                    sampleConnection, sampleTable, commonColumns, discrepancy.Hash, SampleRowsPerDiscrepancy, cancellationToken);

                var sampleNodes = samples
                    .Select(sample => new DataComparisonDetailNode(
                        string.Join(", ", sample.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}")), []))
                    .ToList();

                discrepancyNodes.Add(new DataComparisonDetailNode(
                    $"hash {discrepancy.Hash[..Math.Min(8, discrepancy.Hash.Length)]}...: " +
                    $"source count={discrepancy.SourceCount}, target count={discrepancy.TargetCount}",
                    sampleNodes));
            }

            if (diff.Discrepancies.Count > discrepanciesToShow.Count)
            {
                discrepancyNodes.Add(new DataComparisonDetailNode(
                    $"... {diff.Discrepancies.Count - discrepanciesToShow.Count} more discrepancies not shown.", []));
            }

            return new DataComparisonDetailNode(
                $"{diff.TableName}: source={diff.SourceRowCount}, target={diff.TargetRowCount}, " +
                $"matched={diff.MatchedRowCount}, missing-from-target={diff.RowsMissingFromTarget}, " +
                $"missing-from-source={diff.RowsMissingFromSource}",
                discrepancyNodes);
        }

        /// <summary>
        /// Builds the row-level detail tree for a keyed comparison's differences: rows only in source,
        /// rows only in target, and rows with changed values, each with a capped set of examples.
        /// </summary>
        /// <param name="diff">a DataCompare.Engine.DataComparison.KeyedTableDiffResult holding the comparison to render</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <returns>returns a DataCompare.Engine.Reporting.DataComparisonDetailNode summarizing this table's differences</returns>
        private static DataComparisonDetailNode BuildKeyedTableDiffNode(KeyedTableDiffResult diff, TableSchema sourceTable, TableSchema targetTable)
        {
            var children = new List<DataComparisonDetailNode>();

            if (diff.RowsOnlyInSource.TotalCount > 0)
            {
                children.Add(new DataComparisonDetailNode(
                    $"Rows only in Source ({diff.RowsOnlyInSource.TotalCount})",
                    BuildRowExampleNodes(diff.RowsOnlyInSource, isSourceSide: true, sourceTable)));
            }

            if (diff.RowsOnlyInTarget.TotalCount > 0)
            {
                children.Add(new DataComparisonDetailNode(
                    $"Rows only in Target ({diff.RowsOnlyInTarget.TotalCount})",
                    BuildRowExampleNodes(diff.RowsOnlyInTarget, isSourceSide: false, targetTable)));
            }

            if (diff.ReassignedKeyRows.TotalCount > 0)
            {
                children.Add(new DataComparisonDetailNode(
                    $"Rows with reassigned key ({diff.ReassignedKeyRows.TotalCount})",
                    BuildReassignedKeyRowNodes(diff.ReassignedKeyRows, sourceTable)));
            }

            if (diff.ChangedRows.TotalCount > 0)
            {
                children.Add(new DataComparisonDetailNode(
                    $"Rows with changed values ({diff.ChangedRows.TotalCount})",
                    BuildChangedRowNodes(diff.ChangedRows, sourceTable, targetTable)));
            }

            return new DataComparisonDetailNode(
                $"{diff.TableName}: source={diff.SourceRowCount}, target={diff.TargetRowCount}, " +
                $"matched={diff.MatchedIdenticalCount}, changed={diff.ChangedRows.TotalCount}, " +
                $"missing-from-target={diff.RowsOnlyInSource.TotalCount}, missing-from-source={diff.RowsOnlyInTarget.TotalCount}, " +
                $"reassigned-key={diff.ReassignedKeyRows.TotalCount}",
                children);
        }

        /// <summary>
        /// Builds a capped set of reassigned-key row-example nodes, plus a trailing "N more not shown"
        /// node when the total exceeds the capped example count. Each example carries a <see
        /// cref="DataComparisonGridColumn"/> comparison grid instead of plain child text: the key
        /// column(s) flagged as expected to differ, every non-key column shown as matched (guaranteed
        /// identical by construction — see <see cref="KeyedTableDiffResult.ReconcileReassignedKeys"/>).
        /// </summary>
        /// <param name="examples">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.ReassignedKeyRowExample holding the examples to render</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table, used to detect large-content columns</param>
        /// <returns>returns a System.Collections.Generic.List of DataCompare.Engine.Reporting.DataComparisonDetailNode holding one node per example, plus an overflow node if applicable</returns>
        private static List<DataComparisonDetailNode> BuildReassignedKeyRowNodes(
            CappedExamples<ReassignedKeyRowExample> examples, TableSchema sourceTable)
        {
            var sourceColumnsByName = sourceTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var nodes = examples.Examples.Select(example =>
            {
                var sourceKeyText = string.Join(", ", example.SourceKeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
                var targetKeyText = string.Join(", ", example.TargetKeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

                var gridColumns = new List<DataComparisonGridColumn>();
                gridColumns.AddRange(example.SourceKeyValues.Select(kv => new DataComparisonGridColumn(
                    kv.Key, FormatValue(kv.Value), FormatValue(example.TargetKeyValues[kv.Key]), DataComparisonGridCellKind.ExpectedDifference)));
                gridColumns.AddRange(example.Values.Select(kv =>
                {
                    var displayValue = LargeContentColumn.Is(sourceColumnsByName[kv.Key])
                        ? "(large column — content matches)"
                        : FormatValue(kv.Value);
                    return new DataComparisonGridColumn(kv.Key, displayValue, displayValue, DataComparisonGridCellKind.Matched);
                }));

                return new DataComparisonDetailNode(
                    $"Row moved: source key=[{sourceKeyText}] -> target key=[{targetKeyText}]", [], GridColumns: gridColumns);
            }).ToList();

            if (examples.TotalCount > examples.Examples.Count)
            {
                nodes.Add(new DataComparisonDetailNode($"... {examples.TotalCount - examples.Examples.Count} more not shown.", []));
            }

            return nodes;
        }

        /// <summary>
        /// Builds a capped set of row-example child nodes, plus a trailing "N more not shown" node
        /// when the total exceeds the capped example count. Each example carries a <see
        /// cref="DataComparisonGridColumn"/> comparison grid alongside its plain-text summary: every
        /// column flagged as a real difference, with the row's actual values on the side it exists and
        /// an empty cell on the side it's missing from — the whole row is the difference, not just one
        /// column of it.
        /// </summary>
        /// <param name="examples">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.RowExample holding the examples to render</param>
        /// <param name="isSourceSide">a System.Boolean that is true when these rows exist only in the source (values go in the grid's source cells), false when they exist only in the target</param>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the side the rows were read from, used to detect large-content columns</param>
        /// <returns>returns a System.Collections.Generic.List of DataCompare.Engine.Reporting.DataComparisonDetailNode holding one node per example, plus an overflow node if applicable</returns>
        private static List<DataComparisonDetailNode> BuildRowExampleNodes(
            CappedExamples<RowExample> examples, bool isSourceSide, TableSchema table)
        {
            var columnsByName = table.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var nodes = examples.Examples
                .Select(example =>
                {
                    var gridColumns = example.Values.Select(kv =>
                    {
                        var displayValue = LargeContentColumn.Is(columnsByName[kv.Key]) ? "(large column)" : FormatValue(kv.Value);
                        return isSourceSide
                            ? new DataComparisonGridColumn(kv.Key, displayValue, string.Empty, DataComparisonGridCellKind.RealDifference)
                            : new DataComparisonGridColumn(kv.Key, string.Empty, displayValue, DataComparisonGridCellKind.RealDifference);
                    }).ToList();

                    return new DataComparisonDetailNode(
                        string.Join(", ", example.Values.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}")), [], GridColumns: gridColumns);
                })
                .ToList();

            if (examples.TotalCount > examples.Examples.Count)
            {
                nodes.Add(new DataComparisonDetailNode($"... {examples.TotalCount - examples.Examples.Count} more not shown.", []));
            }

            return nodes;
        }

        /// <summary>
        /// Builds a capped set of changed-row example nodes, plus a trailing "N more not shown" node
        /// when the total exceeds the capped example count. Each example carries a <see
        /// cref="DataComparisonGridColumn"/> comparison grid covering every key and value column — the
        /// key and unchanged columns shown matched, the actually-changed column(s) flagged as a real
        /// difference (unlike the "expected, ignorable" yellow a reassigned key gets). MAX-length binary/
        /// text columns hold a hash, not the real value (see <see cref="LargeContentColumn"/>), so
        /// they're described rather than printed; a changed varbinary column additionally keeps its own
        /// child node carrying a <see cref="DataComparisonLargeContentAction"/> drill-down button
        /// (planning.md §18) — a grid cell can't carry a button, so that one piece stays outside the grid.
        /// </summary>
        /// <param name="examples">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.ChangedRowExample holding the examples to render</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <returns>returns a System.Collections.Generic.List of DataCompare.Engine.Reporting.DataComparisonDetailNode holding one node per example, plus an overflow node if applicable</returns>
        private static List<DataComparisonDetailNode> BuildChangedRowNodes(
            CappedExamples<ChangedRowExample> examples, TableSchema sourceTable, TableSchema targetTable)
        {
            var sourceColumnsByName = sourceTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var nodes = examples.Examples.Select(changedRow =>
            {
                var keyText = string.Join(", ", changedRow.KeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

                var gridColumns = new List<DataComparisonGridColumn>();
                gridColumns.AddRange(changedRow.KeyValues.Select(kv => new DataComparisonGridColumn(
                    kv.Key, FormatValue(kv.Value), FormatValue(kv.Value), DataComparisonGridCellKind.Matched)));

                var valueColumnNames = changedRow.SourceValues.Keys.Where(name => !changedRow.KeyValues.ContainsKey(name));
                gridColumns.AddRange(valueColumnNames.Select(name =>
                {
                    var column = sourceColumnsByName[name];
                    var isChanged = changedRow.ChangedColumnNames.Contains(name);
                    var cellKind = isChanged ? DataComparisonGridCellKind.RealDifference : DataComparisonGridCellKind.Matched;
                    if (!LargeContentColumn.Is(column))
                    {
                        return new DataComparisonGridColumn(
                            name, FormatValue(changedRow.SourceValues[name]), FormatValue(changedRow.TargetValues[name]), cellKind);
                    }

                    var placeholder = isChanged ? "(large column — content differs)" : "(large column — content matches)";
                    return new DataComparisonGridColumn(name, placeholder, placeholder, cellKind);
                }));

                var largeContentActionNodes = changedRow.ChangedColumnNames
                    .Where(name => string.Equals(sourceColumnsByName[name].DataType, "varbinary", StringComparison.OrdinalIgnoreCase)
                                   && LargeContentColumn.Is(sourceColumnsByName[name]))
                    .Select(name => new DataComparisonDetailNode(
                        $"{name}: content differs (large column — hash mismatch)",
                        [],
                        new DataComparisonLargeContentAction(sourceTable, targetTable, name, changedRow.KeyValues)))
                    .ToList();

                return new DataComparisonDetailNode(
                    $"[{keyText}] changed: {string.Join(", ", changedRow.ChangedColumnNames)}",
                    largeContentActionNodes, GridColumns: gridColumns);
            }).ToList();

            if (examples.TotalCount > examples.Examples.Count)
            {
                nodes.Add(new DataComparisonDetailNode($"... {examples.TotalCount - examples.Examples.Count} more not shown.", []));
            }

            return nodes;
        }

        /// <summary>
        /// Formats a column value for display in the detail tree, rendering byte arrays as hex and
        /// null as the literal text "NULL".
        /// </summary>
        /// <param name="value">a nullable System.Object holding the value to format</param>
        /// <returns>returns a System.String containing the formatted value</returns>
        private static string FormatValue(object? value) => value switch
        {
            null => "NULL",
            byte[] bytes => Convert.ToHexString(bytes),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
