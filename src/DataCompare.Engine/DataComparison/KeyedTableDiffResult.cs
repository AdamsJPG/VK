namespace DataCompare.Engine.DataComparison
{

    /// <summary>Result of a primary-key merge-join comparison (see <see cref="KeyedTableComparer"/>).</summary>
    /// <param name="TableName">a System.String holding the schema-qualified name of the table being compared</param>
    /// <param name="SourceRowCount">a System.Int64 holding the total number of rows in the source table</param>
    /// <param name="TargetRowCount">a System.Int64 holding the total number of rows in the target table</param>
    /// <param name="MatchedIdenticalCount">a System.Int64 holding the number of rows whose key matched on both sides and whose non-key values were identical</param>
    /// <param name="RowsOnlyInSource">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.RowExample holding the rows present in the source table but not the target table</param>
    /// <param name="RowsOnlyInTarget">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.RowExample holding the rows present in the target table but not the source table</param>
    /// <param name="ChangedRows">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.ChangedRowExample holding the rows whose key matched on both sides but whose non-key values differ</param>
    /// <param name="OnlyInSourceContentHashCounts">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 tallying every only-in-source row's non-key content hash, uncapped — not for display, used only by <see cref="ReconcileReassignedKeys"/></param>
    /// <param name="OnlyInTargetContentHashCounts">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 tallying every only-in-target row's non-key content hash, uncapped — not for display, used only by <see cref="ReconcileReassignedKeys"/></param>
    /// <param name="ReassignedKeyRows">a DataCompare.Engine.DataComparison.CappedExamples of DataCompare.Engine.DataComparison.ReassignedKeyRowExample holding rows found on both sides with identical non-key values but a different key, populated by <see cref="ReconcileReassignedKeys"/></param>
    public sealed record KeyedTableDiffResult(
        string TableName,
        long SourceRowCount,
        long TargetRowCount,
        long MatchedIdenticalCount,
        CappedExamples<RowExample> RowsOnlyInSource,
        CappedExamples<RowExample> RowsOnlyInTarget,
        CappedExamples<ChangedRowExample> ChangedRows,
        IReadOnlyDictionary<string, long> OnlyInSourceContentHashCounts,
        IReadOnlyDictionary<string, long> OnlyInTargetContentHashCounts,
        CappedExamples<ReassignedKeyRowExample> ReassignedKeyRows)
    {
        /// <summary>
        /// Whether the two tables' data is identical.
        /// </summary>
        /// <returns>returns a System.Boolean that is true when there are no rows only in the source, no rows only in the target, no rows with a reassigned key, and no changed rows</returns>
        public bool IsIdentical =>
            RowsOnlyInSource.TotalCount == 0 && RowsOnlyInTarget.TotalCount == 0
            && ReassignedKeyRows.TotalCount == 0 && ChangedRows.TotalCount == 0;

        /// <summary>
        /// Combines the independent results of comparing several key-range slices of the same table
        /// (see <see cref="KeyRange"/>, planning.md §19) into one result equivalent to comparing the
        /// whole table in a single pass. Reassigned-key reconciliation has not run yet at this point —
        /// a row can move between key-range chunks, so reconciliation only happens once the full-table
        /// view exists (see <see cref="ReconcileReassignedKeys"/>, called after this).
        /// </summary>
        /// <param name="tableName">the table name to report on the combined result</param>
        /// <param name="parts">the per-range results to combine, in any order</param>
        /// <param name="maxExamplesPerCategory">the maximum number of example rows to retain per category in the combined result; exact totals are still tracked beyond this cap</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.KeyedTableDiffResult summarizing all parts together</returns>
        public static KeyedTableDiffResult Combine(string tableName, IReadOnlyList<KeyedTableDiffResult> parts, int maxExamplesPerCategory)
        {
            return new KeyedTableDiffResult(
                tableName,
                parts.Sum(p => p.SourceRowCount),
                parts.Sum(p => p.TargetRowCount),
                parts.Sum(p => p.MatchedIdenticalCount),
                CombineExamples(parts.Select(p => p.RowsOnlyInSource), maxExamplesPerCategory),
                CombineExamples(parts.Select(p => p.RowsOnlyInTarget), maxExamplesPerCategory),
                CombineExamples(parts.Select(p => p.ChangedRows), maxExamplesPerCategory),
                CombineHashCounts(parts.Select(p => p.OnlyInSourceContentHashCounts)),
                CombineHashCounts(parts.Select(p => p.OnlyInTargetContentHashCounts)),
                CombineExamples(parts.Select(p => p.ReassignedKeyRows), maxExamplesPerCategory));
        }

        /// <summary>
        /// Finds only-in-source/only-in-target rows that are actually the same row content reassigned
        /// to a new key, rather than a genuine delete-plus-insert (planning.md addendum — reassigned-key
        /// detection). Must be called once the full-table view exists: after <see cref="Combine"/> for
        /// a range-partitioned large table, or directly on a normal table's result — never per chunk,
        /// since a reassigned row can land in a different key-range chunk than its old key.
        /// </summary>
        /// <param name="keyColumnNames">the key column names, in the same order used to build the compared rows</param>
        /// <param name="valueColumnNames">the non-key column names compared for content equality</param>
        /// <param name="maxExamplesPerCategory">the maximum number of reassigned-key example rows to retain for display; the exact total is tracked regardless of this cap</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.KeyedTableDiffResult with any reconciled rows moved out of <see cref="RowsOnlyInSource"/>/<see cref="RowsOnlyInTarget"/> and into <see cref="ReassignedKeyRows"/></returns>
        public KeyedTableDiffResult ReconcileReassignedKeys(
            IReadOnlyList<string> keyColumnNames, IReadOnlyList<string> valueColumnNames, int maxExamplesPerCategory)
        {
            long reassignedCount = 0;
            foreach (var (hash, sourceCount) in OnlyInSourceContentHashCounts)
            {
                if (OnlyInTargetContentHashCounts.TryGetValue(hash, out var targetCount))
                {
                    reassignedCount += Math.Min(sourceCount, targetCount);
                }
            }

            if (reassignedCount == 0)
            {
                return this;
            }

            // Display examples are re-hashed and paired off the same way, respecting a per-hash count
            // so duplicate-content examples don't all falsely pair with one partner — mirroring the
            // min-count pairing above, just scoped to the small capped example lists rather than every
            // orphan row. A row only shows up here if BOTH its old-key and new-key copies happened to
            // survive into the display cap; the exact reassignedCount above is unaffected by the cap.
            var sourceExamplesByHash = GroupByContentHash(RowsOnlyInSource.Examples, valueColumnNames);
            var targetExamplesByHash = GroupByContentHash(RowsOnlyInTarget.Examples, valueColumnNames);
            var reassignedExamples = new List<ReassignedKeyRowExample>();
            foreach (var (hash, sourceQueue) in sourceExamplesByHash)
            {
                if (!targetExamplesByHash.TryGetValue(hash, out var targetQueue))
                {
                    continue;
                }

                while (sourceQueue.Count > 0 && targetQueue.Count > 0)
                {
                    var sourceExample = sourceQueue.Dequeue();
                    var targetExample = targetQueue.Dequeue();
                    reassignedExamples.Add(new ReassignedKeyRowExample(
                        ExtractColumns(sourceExample.Values, keyColumnNames),
                        ExtractColumns(targetExample.Values, keyColumnNames),
                        ExtractColumns(sourceExample.Values, valueColumnNames)));
                }
            }

            var unmatchedSourceExamples = sourceExamplesByHash.Values.SelectMany(queue => queue).ToList();
            var unmatchedTargetExamples = targetExamplesByHash.Values.SelectMany(queue => queue).ToList();

            return this with
            {
                RowsOnlyInSource = new CappedExamples<RowExample>(unmatchedSourceExamples, RowsOnlyInSource.TotalCount - reassignedCount),
                RowsOnlyInTarget = new CappedExamples<RowExample>(unmatchedTargetExamples, RowsOnlyInTarget.TotalCount - reassignedCount),
                ReassignedKeyRows = new CappedExamples<ReassignedKeyRowExample>(
                    reassignedExamples.Take(maxExamplesPerCategory).ToList(), reassignedCount),
            };
        }

        /// <summary>
        /// Groups a list of row examples by their non-key content hash, into a per-hash queue so equal
        /// counts of duplicate-content examples can be paired off one at a time.
        /// </summary>
        /// <param name="examples">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.DataComparison.RowExample to group</param>
        /// <param name="valueColumnNames">the non-key column names the content hash is computed over</param>
        /// <returns>returns a System.Collections.Generic.Dictionary of System.String to System.Collections.Generic.Queue of DataCompare.Engine.DataComparison.RowExample, keyed by content hash</returns>
        private static Dictionary<string, Queue<RowExample>> GroupByContentHash(
            IReadOnlyList<RowExample> examples, IReadOnlyList<string> valueColumnNames)
        {
            var byHash = new Dictionary<string, Queue<RowExample>>();
            foreach (var example in examples)
            {
                var hash = RowContentHash.Compute(example.Values, valueColumnNames);
                if (!byHash.TryGetValue(hash, out var queue))
                {
                    queue = new Queue<RowExample>();
                    byHash[hash] = queue;
                }

                queue.Enqueue(example);
            }

            return byHash;
        }

        /// <summary>
        /// Extracts a subset of a row's values by column name.
        /// </summary>
        /// <param name="values">a System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object holding the row's full value set</param>
        /// <param name="columnNames">the column names to extract</param>
        /// <returns>returns a System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object containing only the named columns</returns>
        private static IReadOnlyDictionary<string, object?> ExtractColumns(
            IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> columnNames) =>
            columnNames.ToDictionary(name => name, name => values[name]);

        /// <summary>
        /// Sums a content-hash count dictionary across several key-range chunk results, adding counts
        /// for the same hash together.
        /// </summary>
        /// <param name="parts">the per-range hash-count dictionaries to combine</param>
        /// <returns>returns a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 with every part's counts summed by hash</returns>
        private static IReadOnlyDictionary<string, long> CombineHashCounts(IEnumerable<IReadOnlyDictionary<string, long>> parts)
        {
            var combined = new Dictionary<string, long>();
            foreach (var part in parts)
            {
                foreach (var (hash, count) in part)
                {
                    combined[hash] = combined.GetValueOrDefault(hash) + count;
                }
            }

            return combined;
        }

        /// <summary>
        /// Combines several capped example lists for the same category into one, respecting the same
        /// display cap while keeping the exact total count across all parts.
        /// </summary>
        /// <param name="parts">the per-range capped example lists to combine</param>
        /// <param name="maxExamplesPerCategory">the maximum number of examples to retain in the combined list</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.CappedExamples of T combining all parts</returns>
        private static CappedExamples<T> CombineExamples<T>(IEnumerable<CappedExamples<T>> parts, int maxExamplesPerCategory)
        {
            var partsList = parts.ToList();
            var examples = partsList.SelectMany(p => p.Examples).Take(maxExamplesPerCategory).ToList();
            var totalCount = partsList.Sum(p => p.TotalCount);
            return new CappedExamples<T>(examples, totalCount);
        }
    }
}
