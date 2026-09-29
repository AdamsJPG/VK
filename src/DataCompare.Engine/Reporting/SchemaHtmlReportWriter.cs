using System.Net;
using System.Text;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// Generates the self-contained HTML schema comparison reports (inline CSS, no external assets) —
    /// the same grouping and DDL diff shown on screen, exportable to a single file per results tab
    /// (Summary, Tables &amp; Views, Functions &amp; Stored Procedures), each independently.
    /// </summary>
    public static class SchemaHtmlReportWriter
    {
        /// <summary>
        /// Generates the standalone Summary report: the two headline difference percentages plus the
        /// per-object-kind breakdown table, with no per-object DDL detail — the same content shown on
        /// the app's Summary tab.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.String containing the complete HTML document for the summary report</returns>
        public static string GenerateSummary(
            string sourceServer,
            string sourceDatabase,
            string targetServer,
            string targetDatabase,
            SchemaDiffResult result,
            DatabaseSchema sourceSchema,
            DatabaseSchema targetSchema)
        {
            var sourceObjectCount = sourceSchema.Tables.Count + sourceSchema.Views.Count + sourceSchema.Routines.Count;
            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceObjectCount);
            var rows = SchemaObjectTypeSummaryBuilder.Build(result, sourceSchema, targetSchema);

            var body = new StringBuilder();
            body.Append($"""
                <h1>VK Schema Summary</h1>
                {ReportBannerBuilder.Build(sourceServer, sourceDatabase, targetServer, targetDatabase)}
                <p class="summary">Table difference: {tableDifferencePercent:0.0}% — Schema difference: {schemaDifferencePercent:0.0}%</p>

                """);
            body.Append(BuildObjectTypeSummaryTable(rows));

            return WrapDocument("VK Schema Summary Report", body.ToString());
        }

        /// <summary>
        /// Builds the HTML markup for the per-object-kind breakdown table (Type, Source count, Target
        /// count, Only in Source, Only in Target, Different, Diff %).
        /// </summary>
        /// <param name="rows">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.SchemaObjectTypeSummary to render, one per object kind actually read</param>
        /// <returns>returns a System.String containing the HTML markup for the breakdown table</returns>
        private static string BuildObjectTypeSummaryTable(IReadOnlyList<SchemaObjectTypeSummary> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<table class=\"object-type-summary\">\n<thead><tr>");
            sb.Append("<th>Type</th><th>Source count</th><th>Target count</th><th>Only in Source</th>");
            sb.Append("<th>Only in Target</th><th>Different</th><th>Diff %</th>");
            sb.Append("</tr></thead>\n<tbody>\n");

            foreach (var row in rows)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{Encode(row.TypeLabel)}</td>");
                sb.Append($"<td>{row.SourceCount:N0}</td>");
                sb.Append($"<td>{row.TargetCount:N0}</td>");
                sb.Append($"<td>{row.OnlyInSourceCount:N0}</td>");
                sb.Append($"<td>{row.OnlyInTargetCount:N0}</td>");
                sb.Append($"<td>{row.DifferentCount:N0}</td>");
                sb.Append($"<td>{row.DifferencePercent:0.0}%</td>");
                sb.Append("</tr>\n");
            }

            sb.Append("</tbody>\n</table>\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// Generates the standalone Tables &amp; Views report: the only-in-source/only-in-target/
        /// different/identical table and view sections, each rendered as a side-by-side DDL diff.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.String containing the complete HTML document for the tables/views report</returns>
        public static string GenerateTablesAndViews(
            string sourceServer,
            string sourceDatabase,
            string targetServer,
            string targetDatabase,
            SchemaDiffResult result,
            DatabaseSchema sourceSchema,
            DatabaseSchema targetSchema)
        {
            var sourceByName = sourceSchema.Tables.Concat(sourceSchema.Views).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = targetSchema.Tables.Concat(targetSchema.Views).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

            var changedNames = result.TableDiffs.Select(d => d.TableName).ToList();
            var changedSet = new HashSet<string>(changedNames, StringComparer.OrdinalIgnoreCase);
            var identicalNames = sourceByName.Values
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
            body.Append($"""
                <h1>VK Tables &amp; Views Comparison</h1>
                {ReportBannerBuilder.Build(sourceServer, sourceDatabase, targetServer, targetDatabase)}
                <p class="summary">{result.TablesOnlyInSource.Count} table(s)/view(s) only in source, {result.TablesOnlyInTarget.Count} only in target,
                   {result.TableDiffs.Count} with column/definition differences.</p>

                """);

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

            return WrapDocument("VK Tables & Views Report", body.ToString());
        }

        /// <summary>
        /// Generates the standalone Functions &amp; Stored Procedures report: the only-in-source/
        /// only-in-target/different/identical routine sections, each rendered as a side-by-side
        /// definition-text diff — routines have no columns to compare.
        /// </summary>
        /// <param name="sourceServer">a System.String holding the name of the source server</param>
        /// <param name="sourceDatabase">a System.String holding the name of the source database</param>
        /// <param name="targetServer">a System.String holding the name of the target server</param>
        /// <param name="targetDatabase">a System.String holding the name of the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.String containing the complete HTML document for the routines report</returns>
        public static string GenerateRoutines(
            string sourceServer,
            string sourceDatabase,
            string targetServer,
            string targetDatabase,
            SchemaDiffResult result,
            DatabaseSchema sourceSchema,
            DatabaseSchema targetSchema)
        {
            var body = new StringBuilder();
            body.Append($"""
                <h1>VK Functions &amp; Stored Procedures Comparison</h1>
                {ReportBannerBuilder.Build(sourceServer, sourceDatabase, targetServer, targetDatabase)}
                <p class="summary">{result.RoutinesOnlyInSource.Count} routine(s) only in source, {result.RoutinesOnlyInTarget.Count} only in target,
                   {result.RoutineDiffs.Count} with definition differences.</p>

                """);
            body.Append(BuildRoutineSections(result, sourceSchema, targetSchema));

            return WrapDocument("VK Functions & Stored Procedures Report", body.ToString());
        }

        /// <summary>
        /// Builds the report's function and stored procedure sections (Only in Source/Target,
        /// Different, Identical), each object rendered as a side-by-side definition-text diff rather
        /// than the column-based DDL used for tables/views — routines have no columns to compare.
        /// </summary>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the routine differences to render</param>
        /// <param name="sourceSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="targetSchema">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.String containing the HTML markup for every non-empty routine section</returns>
        private static string BuildRoutineSections(SchemaDiffResult result, DatabaseSchema sourceSchema, DatabaseSchema targetSchema)
        {
            var sourceByName = sourceSchema.Routines.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = targetSchema.Routines.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);

            var changedNames = result.RoutineDiffs.Select(d => d.RoutineName).ToList();
            var changedSet = new HashSet<string>(changedNames, StringComparer.OrdinalIgnoreCase);
            var identicalNames = sourceSchema.Routines
                .Where(r => targetByName.ContainsKey(r.FullName) && !changedSet.Contains(r.FullName))
                .Select(r => r.FullName)
                .ToList();

            var sections = new (string Title, List<string> RoutineNames)[]
            {
                ("Only in Source", result.RoutinesOnlyInSource.ToList()),
                ("Only in Target", result.RoutinesOnlyInTarget.ToList()),
                ("Different", changedNames),
                ("Identical", identicalNames),
            };

            var body = new StringBuilder();
            foreach (var (title, routineNames) in sections)
            {
                if (routineNames.Count == 0)
                {
                    continue;
                }

                body.Append($"<h2>{Encode(title)} ({routineNames.Count})</h2>\n");
                foreach (var name in routineNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    sourceByName.TryGetValue(name, out var sourceRoutine);
                    targetByName.TryGetValue(name, out var targetRoutine);
                    body.Append(BuildRoutineSection(name, sourceRoutine, targetRoutine));
                }
            }

            return body.ToString();
        }

        /// <summary>
        /// Builds the HTML markup for one routine's side-by-side definition-text diff.
        /// </summary>
        /// <param name="routineName">a System.String holding the schema-qualified routine name</param>
        /// <param name="sourceRoutine">a DataCompare.Engine.Schema.RoutineSchema describing the source side of the routine, or null when it doesn't exist there</param>
        /// <param name="targetRoutine">a DataCompare.Engine.Schema.RoutineSchema describing the target side of the routine, or null when it doesn't exist there</param>
        /// <returns>returns a System.String containing the HTML markup for this routine's section</returns>
        private static string BuildRoutineSection(string routineName, RoutineSchema? sourceRoutine, RoutineSchema? targetRoutine)
        {
            var kindLabel = (sourceRoutine ?? targetRoutine)!.Kind == RoutineKind.StoredProcedure ? "PROCEDURE" : "FUNCTION";
            var (sourceLines, targetLines) = DefinitionDiffBuilder.BuildDiffLines(sourceRoutine?.Definition, targetRoutine?.Definition);
            return $"""
                <div class="table-block">
                    <h3>{Encode(kindLabel)} {Encode(routineName)}</h3>
                    <div class="ddl-columns">
                        <pre class="ddl-pane">{RenderDefinitionLines(sourceLines)}</pre>
                        <pre class="ddl-pane">{RenderDefinitionLines(targetLines)}</pre>
                    </div>
                </div>

                """;
        }

        /// <summary>
        /// Renders a set of definition-text diff lines as HTML, highlighting any lines flagged as different.
        /// </summary>
        /// <param name="lines">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.DefinitionDiffLine to render</param>
        /// <returns>returns a System.String containing the HTML markup for the rendered lines</returns>
        private static string RenderDefinitionLines(IReadOnlyList<DefinitionDiffLine> lines)
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
        /// <param name="title">a System.String holding the document's &lt;title&gt; text</param>
        /// <param name="body">a System.String holding the HTML markup for the report body</param>
        /// <returns>returns a System.String containing the complete HTML document</returns>
        private static string WrapDocument(string title, string body) => $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <title>{{Encode(title)}}</title>
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
                .hl { background: #FFE6E6; display: block; }
                table.object-type-summary { border-collapse: collapse; margin: 12px 0; }
                table.object-type-summary th, table.object-type-summary td { border: 1px solid #DDD; padding: 6px 12px; text-align: right; font-size: 13px; }
                table.object-type-summary th:first-child, table.object-type-summary td:first-child { text-align: left; }
                table.object-type-summary th { background: #F0F0F0; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
