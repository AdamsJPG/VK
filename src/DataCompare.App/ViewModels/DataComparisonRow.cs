namespace DataCompare.App.ViewModels;

/// <summary>
/// One row in the Data comparison grid — per-table row counts, computed for every common table
/// (identical or not), unlike <see cref="DiffTreeNode"/> which only exists for tables with actual
/// differences. Immutable: built once, when that table's comparison finishes.
/// </summary>
public sealed class DataComparisonRow(
    string tableName,
    long sourceRowCount,
    long targetRowCount,
    long matchedCount,
    long changedCount,
    long missingFromTargetCount,
    long missingFromSourceCount,
    DiffTreeNode? detailNode)
{
    public string TableName { get; } = tableName;
    public long SourceRowCount { get; } = sourceRowCount;
    public long TargetRowCount { get; } = targetRowCount;
    public long MatchedCount { get; } = matchedCount;
    public long ChangedCount { get; } = changedCount;
    public long MissingFromTargetCount { get; } = missingFromTargetCount;
    public long MissingFromSourceCount { get; } = missingFromSourceCount;

    /// <summary>The full comparison detail (row examples, changed columns, etc.) for this table —
    /// null for an identical table, since there's nothing to drill into.</summary>
    public DiffTreeNode? DetailNode { get; } = detailNode;

    public bool HasDifferences => ChangedCount > 0 || MissingFromTargetCount > 0 || MissingFromSourceCount > 0;

    /// <summary>Drives the grid's rollup grouping.</summary>
    public string GroupLabel => HasDifferences ? "Tables with differences" : "Identical tables";
}
