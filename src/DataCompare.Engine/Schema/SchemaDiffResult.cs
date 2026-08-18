namespace DataCompare.Engine.Schema;

public sealed record SchemaDiffResult(
    IReadOnlyList<string> TablesOnlyInSource,
    IReadOnlyList<string> TablesOnlyInTarget,
    IReadOnlyList<TableDiff> TableDiffs)
{
    public bool IsIdentical =>
        TablesOnlyInSource.Count == 0 && TablesOnlyInTarget.Count == 0 && TableDiffs.Count == 0;
}
