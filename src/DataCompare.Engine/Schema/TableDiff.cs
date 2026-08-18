namespace DataCompare.Engine.Schema;

public sealed record TableDiff(
    string TableName,
    IReadOnlyList<string> ColumnsOnlyInSource,
    IReadOnlyList<string> ColumnsOnlyInTarget,
    IReadOnlyList<ColumnChange> ChangedColumns)
{
    public bool HasDifferences =>
        ColumnsOnlyInSource.Count > 0 || ColumnsOnlyInTarget.Count > 0 || ChangedColumns.Count > 0;
}
