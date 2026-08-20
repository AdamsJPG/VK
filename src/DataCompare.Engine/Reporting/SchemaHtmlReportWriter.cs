using System.Net;
using System.Text;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// Generates a self-contained HTML schema comparison report (inline CSS, no external assets) —
    /// the same grouping and DDL diff shown on screen, exportable to a single file.
    /// </summary>
    public static class SchemaHtmlReportWriter
    {
        /// <summary>
        /// Generates the complete self-contained HTML schema comparison report.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.String containing the complete HTML document for the schema comparison report</returns>
        public static string Generate(
            string sourceServer,
            string sourceDatabase,
            string targetServer,
            string targetDatabase,
            SchemaDiffResult result,
            DatabaseSchema sourceSchema,
            DatabaseSchema targetSchema)
        {
            var sourceByName = sourceSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = targetSchema.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

            var changedNames = result.TableDiffs.Select(d => d.TableName).ToList();
            var changedSet = new HashSet<string>(changedNames, StringComparer.OrdinalIgnoreCase);
            var identicalNames = sourceSchema.Tables
                .Where(t => targetByName.ContainsKey(t.FullName) && !changedSet.Contains(t.FullName))
                .Select(t => t.FullName)
                .ToList();

            var sections = new (string Title, List<string> TableNames)[]
            {
                ("Only in Source", result.TablesOnlyInSource.ToList()),
                ("Only in Target", result.TablesOnlyInTarget.ToList()),
                ("Different", changedNames),
                ("Identical", identicalNames),
            };

            var body = new StringBuilder();
            body.Append(BuildHeader(sourceServer, sourceDatabase, targetServer, targetDatabase, result, sourceSchema.Tables.Count));

            foreach (var (title, tableNames) in sections)
            {
                if (tableNames.Count == 0)
                {
                    continue;
                }

                body.Append($"<h2>{Encode(title)} ({tableNames.Count})</h2>\n");
                foreach (var name in tableNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    sourceByName.TryGetValue(name, out var sourceTable);
                    targetByName.TryGetValue(name, out var targetTable);
                    body.Append(BuildTableSection(name, sourceTable, targetTable));
                }
            }

            return WrapDocument(body.ToString());
        }

        /// <summary>
        /// Builds the report's header banner, showing the source and target connection details and the
        /// overall table-difference summary line.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the counts to summarize</param>
        /// <param name="sourceTableCount">a System.Int32 holding the total number of tables in the source database, used to compute the difference-percentage line</param>
        /// <returns>returns a System.String containing the HTML markup for the header</returns>
        private static string BuildHeader(
            string sourceServer, string sourceDatabase, string targetServer, string targetDatabase,
            SchemaDiffResult result, int sourceTableCount)
        {
            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceTableCount);
            return $"""
                <h1>VK Schema Comparison</h1>
                {ReportBannerBuilder.Build(sourceServer, sourceDatabase, targetServer, targetDatabase)}
                <p class="summary">{result.TablesOnlyInSource.Count} table(s) only in source, {result.TablesOnlyInTarget.Count} only in target,
                   {result.TableDiffs.Count} table(s) with column differences.</p>
                <p class="summary">Table difference: {tableDifferencePercent:0.0}% — Schema difference: {schemaDifferencePercent:0.0}%</p>

                """;
        }

        /// <summary>
        /// Builds the HTML markup for one table's side-by-side DDL comparison.
        /// </summary>
        /// <param name="tableName">a System.String holding the schema-qualified table name</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table, or null when the table doesn't exist there</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table, or null when the table doesn't exist there</param>
        /// <returns>returns a System.String containing the HTML markup for this table's section</returns>
        private static string BuildTableSection(string tableName, TableSchema? sourceTable, TableSchema? targetTable)
        {
            var (sourceLines, targetLines) = TableDdlDiffBuilder.BuildDiffLines(sourceTable, targetTable);
            return $"""
                <div class="table-block">
                    <h3>{Encode(tableName)}</h3>
                    <div class="ddl-columns">
                        <pre class="ddl-pane">{RenderLines(sourceLines)}</pre>
                        <pre class="ddl-pane">{RenderLines(targetLines)}</pre>
                    </div>
                </div>

                """;
        }

        /// <summary>
        /// Renders a set of DDL diff lines as HTML, highlighting any lines flagged as different.
        /// </summary>
        /// <param name="lines">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.TableDdlDiffLine to render</param>
        /// <returns>returns a System.String containing the HTML markup for the rendered lines</returns>
        private static string RenderLines(IReadOnlyList<TableDdlDiffLine> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var cssClass = line.IsHighlighted ? " class=\"hl\"" : string.Empty;
                sb.Append($"<span{cssClass}>{Encode(line.Text)}</span>\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// HTML-encodes a string of text for safe inclusion in the report markup.
        /// </summary>
        /// <param name="text">a System.String holding the text to encode</param>
        /// <returns>returns a System.String containing the HTML-encoded text</returns>
        private static string Encode(string text) => WebUtility.HtmlEncode(text);

        /// <summary>
        /// Wraps the report body markup in a complete, self-contained HTML document with inline CSS.
        /// </summary>
        /// <param name="body">a System.String holding the HTML markup for the report body</param>
        /// <returns>returns a System.String containing the complete HTML document</returns>
        private static string WrapDocument(string body) => $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <title>VK Schema Comparison Report</title>
            <style>
                body { font-family: 'Segoe UI', Arial, sans-serif; margin: 24px; color: #222; background: #fff; }
                h1 { margin-bottom: 4px; }
                h2 { margin-top: 32px; border-bottom: 2px solid #E8792A; padding-bottom: 4px; }
                h3 { margin-top: 20px; margin-bottom: 4px; font-family: Consolas, monospace; }
                {{ReportBannerBuilder.Css}}
                .summary { color: #444; margin: 10px 0 8px; }
                .table-block { margin-bottom: 16px; }
                .ddl-columns { display: flex; gap: 12px; }
                .ddl-pane { flex: 1; background: #F7F7F7; border: 1px solid #DDD; padding: 8px;
                             font-family: Consolas, monospace; font-size: 13px; white-space: pre-wrap; overflow-x: auto; margin: 0; }
                .hl { background: #D6F5D6; display: block; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
