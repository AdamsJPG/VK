namespace DataCompare.Engine.DataComparison;

/// <summary>Result of a primary-key merge-join comparison (see <see cref="KeyedTableComparer"/>).</summary>
public sealed record KeyedTableDiffResult(
    string TableName,
    long SourceRowCount,
    long TargetRowCount,
    long MatchedIdenticalCount,
    CappedExamples<RowExample> RowsOnlyInSource,
    CappedExamples<RowExample> RowsOnlyInTarget,
    CappedExamples<ChangedRowExample> ChangedRows)
{
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
