using System.Net;
using System.Text;

namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// Generates a self-contained HTML data-comparison report (inline CSS, no external assets) — the
    /// same rollup grouping and unmissable difference styling shown on screen, exportable to a single
    /// file. A sibling to <see cref="SchemaHtmlReportWriter"/>, not a mode of it: schema and data are
    /// answering two different questions (does the table structure match vs. does the data match), so
    /// they get separate reports rather than one export button silently only ever covering one of them.
    /// </summary>
    public static class DataComparisonHtmlReportWriter
    {
        /// <summary>
        /// generates the complete self-contained HTML data-comparison report for the given rows.
        /// </summary>
        /// <param name="sourceServer">a string containing the name of the source server</param>
        /// <param name="sourceDatabase">a string containing the name of the source database</param>
        /// <param name="targetServer">a string containing the name of the target server</param>
        /// <param name="targetDatabase">a string containing the name of the target database</param>
        /// <param name="rows">an IReadOnlyList where T is a DataCompare.Engine.Reporting.DataComparisonTableSummary object, containing the per-table row-count summaries to render</param>
        /// <returns>returns a string containing the complete HTML document for the data comparison report</returns>
        public static string Generate(
            string sourceServer, string sourceDatabase, string targetServer, string targetDatabase,
            IReadOnlyList<DataComparisonTableSummary> rows)
        {
            var differing = rows.Where(r => r.HasDifferences).OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase).ToList();
            var identical = rows.Where(r => !r.HasDifferences).OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase).ToList();

            var body = new StringBuilder();
            body.Append(BuildHeader(sourceServer, sourceDatabase, targetServer, targetDatabase, rows.Count, differing.Count));

            // <details>/<summary> is native, JS-free collapse — differences open by default since that's
            // the whole point of running this; identical tables closed by default so 30 rows nobody needs
            // to read don't bury the 3 that do (planning.md §20 addendum).
            if (differing.Count > 0)
            {
                body.Append($"<details open class=\"differences\"><summary>Tables with differences ({differing.Count})</summary>\n");
                body.Append(BuildTable(differing));
                body.Append("</details>\n\n");
            }

            if (identical.Count > 0)
            {
                body.Append($"<details><summary>Identical tables ({identical.Count})</summary>\n");
                body.Append(BuildTable(identical));
                body.Append("</details>\n\n");
            }

            return WrapDocument(body.ToString());
        }

        // A simplified twin-cylinder database icon (stacked disks) — not pixel-identical to the app's
        // WPF geometry, but the same idea, since there's no reason to reproduce that path data for a
        // one-off HTML icon. currentColor so the same markup works on both the grey source panel and
        // the white icon needed against the orange target panel.
        private const string DatabaseIconSvg = """
            <svg viewBox="0 0 100 70" width="40" height="28" class="db-icon">
                <path d="M15,15 L15,55 A35,12 0 0,0 85,55 L85,15" fill="currentColor" />
                <ellipse cx="50" cy="15" rx="35" ry="12" fill="currentColor" />
                <ellipse cx="50" cy="35" rx="35" ry="12" fill="none" stroke="white" stroke-width="3" />
                <ellipse cx="50" cy="55" rx="35" ry="12" fill="none" stroke="white" stroke-width="3" />
            </svg>
            """;

        /// <summary>
        /// builds the report's header banner, showing the source and target connection details and the overall summary line.
        /// </summary>
        /// <param name="sourceServer">a string containing the name of the source server</param>
        /// <param name="sourceDatabase">a string containing the name of the source database</param>
        /// <param name="targetServer">a string containing the name of the target server</param>
        /// <param name="targetDatabase">a string containing the name of the target database</param>
        /// <param name="totalTables">an int containing the total number of tables compared</param>
        /// <param name="differingTables">an int containing the number of tables with data differences</param>
        /// <returns>returns a string containing the HTML markup for the header banner</returns>
        private static string BuildHeader(
            string sourceServer, string sourceDatabase, string targetServer, string targetDatabase,
            int totalTables, int differingTables) => $"""
            <h1>VK Data Comparison</h1>
            <div class="banner">
                <div class="side source">
                    {DatabaseIconSvg}
                    <div class="side-text">
                        <div class="side-label">Source</div>
                        <div class="server">{Encode(sourceServer)}</div>
                        <div class="database">{Encode(sourceDatabase)}</div>
                    </div>
                </div>
                <div class="side target">
                    <div class="side-text">
                        <div class="side-label">Target</div>
                        <div class="server">{Encode(targetServer)}</div>
                        <div class="database">{Encode(targetDatabase)}</div>
                    </div>
                    {DatabaseIconSvg}
                </div>
            </div>
            <p class="summary">Compared {totalTables} table(s) — {differingTables} table(s) with data differences.</p>

            """;

        /// <summary>
        /// builds the HTML table markup listing row-count summaries for the given tables.
        /// </summary>
        /// <param name="rows">an IReadOnlyList where T is a DataCompare.Engine.Reporting.DataComparisonTableSummary object, containing the rows to render</param>
        /// <returns>returns a string containing the HTML markup for the table</returns>
        private static string BuildTable(IReadOnlyList<DataComparisonTableSummary> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<table class=\"data-rows\">\n<thead><tr>");
            sb.Append("<th>Table</th><th>Source rows</th><th>Target rows</th><th>Matched</th>");
            sb.Append("<th>Changed</th><th>Missing → Target</th><th>Missing → Source</th><th>Reassigned key</th>");
            sb.Append("</tr></thead>\n<tbody>\n");

            foreach (var row in rows)
            {
                var rowClass = row.HasDifferences ? " class=\"differs\"" : string.Empty;
                sb.Append($"<tr{rowClass}>");
                sb.Append($"<td>{Encode(row.TableName)}</td>");
                sb.Append($"<td>{row.SourceRowCount:N0}</td>");
                sb.Append($"<td>{row.TargetRowCount:N0}</td>");
                sb.Append($"<td>{row.MatchedCount:N0}</td>");
                sb.Append(BuildCountCell(row.ChangedCount));
                sb.Append(BuildCountCell(row.MissingFromTargetCount));
                sb.Append(BuildCountCell(row.MissingFromSourceCount));
                sb.Append(BuildCountCell(row.ReassignedKeyCount));
                sb.Append("</tr>\n");

                // Row-level drill-down (row examples, changed columns) in a full-width row right below
                // the summary row — the same detail the on-screen grid shows when you select a table,
                // now in the export too (previously the export only ever showed the summary counts).
                if (row.Detail is { Children.Count: > 0 } detail)
                {
                    sb.Append("<tr class=\"detail-row\"><td colspan=\"8\">");
                    sb.Append("<details><summary>Show row-level detail</summary>");
                    sb.Append(RenderDetailNodes(detail.Children));
                    sb.Append("</details></td></tr>\n");
                }
            }

            sb.Append("</tbody>\n</table>\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// builds a single table cell for a count value, flagging non-zero counts with the "nonzero" CSS class.
        /// </summary>
        /// <param name="count">a long containing the count value to render</param>
        /// <returns>returns a string containing the HTML markup for the table cell</returns>
        private static string BuildCountCell(long count) =>
            count > 0 ? $"<td class=\"nonzero\">{count:N0}</td>" : $"<td>{count:N0}</td>";

        /// <summary>
        /// Recursively renders a table's row-level detail tree as nested lists — a node with children
        /// (e.g. "Rows with changed values (2)") renders as its own collapsible &lt;details&gt; group; a
        /// node with a comparison grid (a reassigned-key row example) renders that grid instead; a plain
        /// leaf node (e.g. one row's changed-column text) renders as a plain list item.
        /// </summary>
        /// <param name="nodes">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Reporting.DataComparisonDetailNode to render</param>
        /// <returns>returns a System.String containing the HTML markup for this level of the detail tree</returns>
        private static string RenderDetailNodes(IReadOnlyList<DataComparisonDetailNode> nodes)
        {
            var sb = new StringBuilder();
            sb.Append("<ul class=\"detail-tree\">\n");
            foreach (var node in nodes)
            {
                if (node.GridColumns.Count > 0)
                {
                    sb.Append("<li>").Append(Encode(node.Text));
                    sb.Append(RenderComparisonGrid(node.GridColumns));
                    sb.Append("</li>\n");
                }
                else if (node.Children.Count > 0)
                {
                    sb.Append("<li><details><summary>").Append(Encode(node.Text)).Append("</summary>");
                    sb.Append(RenderDetailNodes(node.Children));
                    sb.Append("</details></li>\n");
                }
                else
                {
                    sb.Append("<li>").Append(Encode(node.Text)).Append("</li>\n");
                }
            }

            sb.Append("</ul>\n");
            return sb.ToString();
        }

        /// <summary>
        /// Renders a row's source/target comparison grid as a single-row table: one header row with
        /// every column name shown twice (the source group, then the target group), and one data row
        /// with the source side's values on the left and the target side's values on the right — not
        /// source/target stacked as separate rows. Each column is flagged matched, expected-to-differ
        /// (soft yellow), or a real difference (loud red) per <see cref="DataComparisonGridCellKind"/>.
        /// </summary>
        /// <param name="columns">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Reporting.DataComparisonGridColumn holding the columns to render</param>
        /// <returns>returns a System.String containing the HTML markup for the comparison grid</returns>
        private static string RenderComparisonGrid(IReadOnlyList<DataComparisonGridColumn> columns)
        {
            var sb = new StringBuilder();
            sb.Append("<table class=\"comparison-grid\">\n");
            sb.Append($"<tr><th colspan=\"{columns.Count}\" class=\"source-group\">Source</th>");
            sb.Append($"<th colspan=\"{columns.Count}\" class=\"target-group\">Target</th></tr>\n");
            sb.Append("<tr>");
            foreach (var column in columns)
            {
                sb.Append("<th>").Append(Encode(column.ColumnName)).Append("</th>");
            }

            foreach (var column in columns)
            {
                sb.Append("<th>").Append(Encode(column.ColumnName)).Append("</th>");
            }

            sb.Append("</tr>\n<tr>");
            foreach (var column in columns)
            {
                sb.Append($"<td class=\"{GridCellCssClass(column.CellKind)}\">").Append(Encode(column.SourceValueDisplay)).Append("</td>");
            }

            foreach (var column in columns)
            {
                sb.Append($"<td class=\"{GridCellCssClass(column.CellKind)}\">").Append(Encode(column.TargetValueDisplay)).Append("</td>");
            }

            sb.Append("</tr>\n</table>\n");
            return sb.ToString();
        }

        /// <summary>
        /// maps a comparison-grid cell's kind to the CSS class that styles it.
        /// </summary>
        /// <param name="cellKind">a DataCompare.Engine.Reporting.DataComparisonGridCellKind describing how the cell should be highlighted</param>
        /// <returns>returns a System.String containing the CSS class name</returns>
        private static string GridCellCssClass(DataComparisonGridCellKind cellKind) => cellKind switch
        {
            DataComparisonGridCellKind.ExpectedDifference => "key-cell",
            DataComparisonGridCellKind.RealDifference => "diff-cell",
            _ => "match-cell",
        };

        /// <summary>
        /// HTML-encodes a string of text for safe inclusion in the report markup.
        /// </summary>
        /// <param name="text">a string containing the text to encode</param>
        /// <returns>returns a string containing the HTML-encoded text</returns>
        private static string Encode(string text) => WebUtility.HtmlEncode(text);

        /// <summary>
        /// wraps the report body markup in a complete, self-contained HTML document with inline CSS.
        /// </summary>
        /// <param name="body">a string containing the HTML markup for the report body</param>
        /// <returns>returns a string containing the complete HTML document</returns>
        private static string WrapDocument(string body) => $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <title>VK Data Comparison Report</title>
            <style>
                body { font-family: 'Segoe UI', Arial, sans-serif; margin: 24px; color: #222; background: #fff; }
                h1 { margin-bottom: 12px; }
                .banner { display: flex; border-radius: 4px; overflow: hidden; }
                .side { flex: 1; display: flex; align-items: center; gap: 12px; padding: 14px 20px; }
                .side.source { background: #F0F0F0; color: #222; }
                .side.target { background: #E8792A; color: #fff; justify-content: flex-end; text-align: right; }
                .side .db-icon { flex-shrink: 0; }
                .side.source .db-icon { color: #333; }
                .side.target .db-icon { color: #fff; }
                .side-label { font-weight: bold; font-size: 15px; }
                .side .server { font-size: 11px; opacity: 0.75; }
                .side .database { font-weight: 600; font-size: 13px; }
                .summary { color: #444; margin: 10px 0 4px; }
                details { margin-top: 20px; }
                summary { cursor: pointer; font-size: 17px; font-weight: bold; border-bottom: 2px solid #E8792A;
                          padding-bottom: 4px; margin-bottom: 8px; }
                details.differences summary { color: #B00000; border-bottom-color: #B00000; }
                table.data-rows { border-collapse: collapse; width: 100%; margin-bottom: 8px; }
                table.data-rows th, table.data-rows td { border: 1px solid #DDD; padding: 6px 10px; text-align: right; font-size: 13px; }
                table.data-rows th:first-child, table.data-rows td:first-child { text-align: left; font-family: Consolas, monospace; }
                table.data-rows th { background: #F0F0F0; }
                tr.differs { background: #FFE6E6; font-weight: bold; }
                td.nonzero { color: #B00000; font-weight: bold; }
                tr.detail-row td { background: #FAFAFA; text-align: left; font-weight: normal; padding: 4px 10px 10px; }
                tr.detail-row summary { font-size: 13px; font-weight: 600; color: #444; border-bottom: none;
                                         margin-bottom: 4px; padding-bottom: 0; }
                ul.detail-tree { list-style: none; margin: 4px 0 4px 16px; padding: 0; border-left: 2px solid #EEE; }
                ul.detail-tree li { margin: 3px 0; padding-left: 10px; font-family: Consolas, monospace; font-size: 12px; color: #333; }
                ul.detail-tree li > details > summary { font-family: 'Segoe UI', Arial, sans-serif; font-size: 13px;
                                                          font-weight: 600; color: #444; }
                table.comparison-grid { border-collapse: collapse; margin: 6px 0 4px; font-family: 'Segoe UI', Arial, sans-serif; }
                table.comparison-grid th, table.comparison-grid td { border: 1px solid #DDD; padding: 4px 10px; font-size: 12px; text-align: center; }
                table.comparison-grid th { background: #F0F0F0; font-weight: 600; }
                table.comparison-grid th.source-group { background: #F0F0F0; color: #222; }
                table.comparison-grid th.target-group { background: #E8792A; color: #fff; }
                table.comparison-grid td.key-cell { background: #FFF3CD; }
                table.comparison-grid td.match-cell { background: #E6F4EA; }
                table.comparison-grid td.diff-cell { background: #FFE6E6; color: #B00000; font-weight: bold; }
            </style>
            </head>
            <body>
            {{body}}
            </body>
            </html>
            """;
    }
}
