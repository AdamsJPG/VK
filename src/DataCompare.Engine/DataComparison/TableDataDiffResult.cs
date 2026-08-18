namespace DataCompare.Engine.DataComparison;

public sealed record TableDataDiffResult(
    string TableName,
    long SourceRowCount,
    long TargetRowCount,
    long MatchedRowCount,
    IReadOnlyList<HashCountDiscrepancy> Discrepancies)
{
    public bool IsIdentical => Discrepancies.Count == 0 && SourceRowCount == TargetRowCount;
    public long RowsMissingFromTarget => Discrepancies.Sum(d => d.MissingFromTarget);
    public long RowsMissingFromSource => Discrepancies.Sum(d => d.MissingFromSource);
}
