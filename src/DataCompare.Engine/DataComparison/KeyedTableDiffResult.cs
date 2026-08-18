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
}
