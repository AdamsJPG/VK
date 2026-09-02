namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Builds side-by-side DDL lines for one table, with per-line highlighting — shared by the on-screen
    /// detail pane and the HTML report so both show exactly the same diff.
    /// </summary>
    public static class TableDdlDiffBuilder
    {
        /// <summary>
        /// Builds the side-by-side DDL lines for one table, highlighting lines that differ between
        /// source and target, or the whole table when it only exists on one side.
        /// </summary>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table, or null when it doesn't exist there</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table, or null when it doesn't exist there</param>
        /// <returns>returns a System.ValueTuple of two System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.TableDdlDiffLine, one per side</returns>
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
                new($"CREATE {HeaderKeyword(sourceTable.Kind)} [{sourceTable.SchemaName}].[{sourceTable.TableName}]", false),
                new("(", false),
            };
            var targetLines = new List<TableDdlDiffLine>
            {
                new($"CREATE {HeaderKeyword(targetTable.Kind)} [{targetTable.SchemaName}].[{targetTable.TableName}]", false),
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

        /// <summary>
        /// Generates the full DDL lines for a table that only exists on one side, optionally
        /// highlighting every line.
        /// </summary>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to generate DDL for</param>
        /// <param name="highlightAll">a System.Boolean that, when true, marks every generated line as highlighted</param>
        /// <returns>returns a System.Collections.Generic.List of DataCompare.Engine.Schema.TableDdlDiffLine containing the full DDL</returns>
        private static List<TableDdlDiffLine> GenerateFullDdl(TableSchema table, bool highlightAll)
        {
            var lines = new List<TableDdlDiffLine>
            {
                new($"CREATE {HeaderKeyword(table.Kind)} [{table.SchemaName}].[{table.TableName}]", highlightAll),
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

        /// <summary>
        /// Determines the DDL header keyword for a table-like object's kind.
        /// </summary>
        /// <param name="kind">a DataCompare.Engine.Schema.SchemaObjectKind indicating whether the object is a table or a view</param>
        /// <returns>returns a System.String holding "TABLE" or "VIEW"</returns>
        private static string HeaderKeyword(SchemaObjectKind kind) => kind == SchemaObjectKind.View ? "VIEW" : "TABLE";
    }
}
