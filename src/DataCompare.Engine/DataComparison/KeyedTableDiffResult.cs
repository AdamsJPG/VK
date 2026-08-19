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
    public sealed record KeyedTableDiffResult(
        string TableName,
        long SourceRowCount,
        long TargetRowCount,
        long MatchedIdenticalCount,
        CappedExamples<RowExample> RowsOnlyInSource,
        CappedExamples<RowExample> RowsOnlyInTarget,
        CappedExamples<ChangedRowExample> ChangedRows)
    {
        /// <summary>
        /// Whether the two tables' data is identical.
        /// </summary>
        /// <returns>returns a System.Boolean that is true when there are no rows only in the source, no rows only in the target, and no changed rows</returns>
        public bool IsIdentical =>
            RowsOnlyInSource.TotalCount == 0 && RowsOnlyInTarget.TotalCount == 0 && ChangedRows.TotalCount == 0;

        /// <summary>
        /// Combines the independent results of comparing several key-range slices of the same table
        /// (see <see cref="KeyRange"/>, planning.md §19) into one result equivalent to comparing the
        /// whole table in a single pass.
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
                CombineExamples(parts.Select(p => p.ChangedRows), maxExamplesPerCategory));
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
