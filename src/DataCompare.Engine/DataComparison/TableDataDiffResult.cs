namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Result of a hash-count comparison of a table's data between source and target, without a
    /// primary key to merge-join on — differences are reported as row-content hashes whose
    /// occurrence counts differ, rather than as individual matched/unmatched rows.
    /// </summary>
    /// <param name="TableName">a System.String holding the schema-qualified name of the table being compared</param>
    /// <param name="SourceRowCount">a System.Int64 holding the total number of rows in the source table</param>
    /// <param name="TargetRowCount">a System.Int64 holding the total number of rows in the target table</param>
    /// <param name="MatchedRowCount">a System.Int64 holding the number of rows whose content hash occurs the same number of times in both tables</param>
    /// <param name="Discrepancies">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.DataComparison.HashCountDiscrepancy holding the row-content hashes whose occurrence counts differ between source and target</param>
    public sealed record TableDataDiffResult(
        string TableName,
        long SourceRowCount,
        long TargetRowCount,
        long MatchedRowCount,
        IReadOnlyList<HashCountDiscrepancy> Discrepancies)
    {
        /// <summary>
        /// Whether the two tables' data is identical.
        /// </summary>
        /// <returns>returns a System.Boolean that is true when there are no hash-count discrepancies and both tables have the same row count</returns>
        public bool IsIdentical => Discrepancies.Count == 0 && SourceRowCount == TargetRowCount;

        /// <summary>
        /// The total number of rows present in the source table but missing from the target table, summed across all discrepancies.
        /// </summary>
        /// <returns>returns a System.Int64 holding the total count of rows missing from the target table</returns>
        public long RowsMissingFromTarget => Discrepancies.Sum(d => d.MissingFromTarget);

        /// <summary>
        /// The total number of rows present in the target table but missing from the source table, summed across all discrepancies.
        /// </summary>
        /// <returns>returns a System.Int64 holding the total count of rows missing from the source table</returns>
        public long RowsMissingFromSource => Discrepancies.Sum(d => d.MissingFromSource);
    }
}
