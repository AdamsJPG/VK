namespace DataCompare.Engine.DataComparison;

/// <summary>One row-content hash whose occurrence count differs between source and target.</summary>
public sealed record HashCountDiscrepancy(string Hash, long SourceCount, long TargetCount)
{
    public long MissingFromTarget => Math.Max(0, SourceCount - TargetCount);
    public long MissingFromSource => Math.Max(0, TargetCount - SourceCount);
}
