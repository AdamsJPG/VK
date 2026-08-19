namespace DataCompare.Engine.Reporting;

/// <summary>Per-table row-count summary for the Data comparison HTML report — deliberately a plain
/// Engine-layer DTO (not the App layer's view model type) so this project stays independent of the
/// UI. Exists for every common table, identical or not.</summary>
public sealed record DataComparisonTableSummary(
    string TableName,
    long SourceRowCount,
    long TargetRowCount,
    long MatchedCount,
    long ChangedCount,
    long MissingFromTargetCount,
    long MissingFromSourceCount)
{
    public bool HasDifferences => ChangedCount > 0 || MissingFromTargetCount > 0 || MissingFromSourceCount > 0;
}
