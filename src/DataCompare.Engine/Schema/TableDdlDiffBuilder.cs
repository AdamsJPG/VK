namespace DataCompare.Engine.Schema;

/// <summary>
/// Builds side-by-side DDL lines for one table, with per-line highlighting — shared by the on-screen
/// detail pane and the HTML report so both show exactly the same diff.
/// </summary>
public static class TableDdlDiffBuilder
{
    public static (IReadOnlyList<TableDdlDiffLine> Source, IReadOnlyList<TableDdlDiffLine> Target) BuildDiffLines(
        TableSchema? sourceTable, TableSchema? targetTable)
    {
        if (sourceTable is null && targetTable is null)
        {
            return ([], []);
        }

        if (targetTable is null)
        {
            return (GenerateFullDdl(sourceTable!, highlightAll: true), []);
        }

        if (sourceTable is null)
        {
            return ([], GenerateFullDdl(targetTable, highlightAll: true));
        }

        var sourceLines = new List<TableDdlDiffLine>
        {
            new($"CREATE TABLE [{sourceTable.SchemaName}].[{sourceTable.TableName}]", false),
            new("(", false),
        };
        var targetLines = new List<TableDdlDiffLine>
        {
            new($"CREATE TABLE [{targetTable.SchemaName}].[{targetTable.TableName}]", false),
            new("(", false),
        };

        var sourceColumnsByName = sourceTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetColumnsByName = targetTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var orderedNames = sourceTable.Columns.Select(c => c.Name).ToList();
        orderedNames.AddRange(targetTable.Columns.Select(c => c.Name)
            .Where(name => !orderedNames.Contains(name, StringComparer.OrdinalIgnoreCase)));

        for (var i = 0; i < orderedNames.Count; i++)
        {
            var name = orderedNames[i];
            var suffix = i == orderedNames.Count - 1 ? string.Empty : ",";
            sourceColumnsByName.TryGetValue(name, out var sourceColumn);
            targetColumnsByName.TryGetValue(name, out var targetColumn);

            if (sourceColumn is not null && targetColumn is not null)
            {
                var sourceText = TableDdlGenerator.FormatColumnLine(sourceColumn);
                var targetText = TableDdlGenerator.FormatColumnLine(targetColumn);
                var isChanged = !string.Equals(sourceText, targetText, StringComparison.Ordinal);
                sourceLines.Add(new TableDdlDiffLine($"    {sourceText}{suffix}", isChanged));
                targetLines.Add(new TableDdlDiffLine($"    {targetText}{suffix}", isChanged));
            }
            else if (sourceColumn is not null)
            {
                sourceLines.Add(new TableDdlDiffLine($"    {TableDdlGenerator.FormatColumnLine(sourceColumn)}{suffix}", true));
                targetLines.Add(TableDdlDiffLine.Blank());
            }
            else if (targetColumn is not null)
            {
                sourceLines.Add(TableDdlDiffLine.Blank());
                targetLines.Add(new TableDdlDiffLine($"    {TableDdlGenerator.FormatColumnLine(targetColumn)}{suffix}", true));
            }
        }

        sourceLines.Add(new TableDdlDiffLine(")", false));
        targetLines.Add(new TableDdlDiffLine(")", false));

        return (sourceLines, targetLines);
    }

    private static List<TableDdlDiffLine> GenerateFullDdl(TableSchema table, bool highlightAll)
    {
        var lines = new List<TableDdlDiffLine>
        {
            new($"CREATE TABLE [{table.SchemaName}].[{table.TableName}]", highlightAll),
            new("(", highlightAll),
        };

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var suffix = i == table.Columns.Count - 1 ? string.Empty : ",";
            lines.Add(new TableDdlDiffLine($"    {TableDdlGenerator.FormatColumnLine(table.Columns[i])}{suffix}", highlightAll));
        }

        lines.Add(new TableDdlDiffLine(")", highlightAll));
        return lines;
    }
}
