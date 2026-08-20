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
                body.Append(
                    $"<details open class=\"differences\"><summary>Tables with differences " +
                    $"(<span id=\"differences-count\">{differing.Count}</span>)</summary>\n");
                body.Append(BuildTable(differing, assignRowIds: true));
                body.Append("</details>\n\n");
            }

            if (identical.Count > 0)
            {
                body.Append(
                    $"<details><summary>Identical tables (<span id=\"identical-count\">{identical.Count}</span>)</summary>\n");
                body.Append(BuildTable(identical, assignRowIds: false));
                body.Append("</details>\n\n");
            }

            return WrapDocument(body.ToString());
        }

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
            {ReportBannerBuilder.Build(sourceServer, sourceDatabase, targetServer, targetDatabase)}
            <p class="summary">Compared {totalTables} table(s) — {differingTables} table(s) with data differences.</p>

            """;

        /// <summary>
        /// builds the HTML table markup listing row-count summaries for the given tables.
        /// </summary>
        /// <param name="rows">an IReadOnlyList where T is a DataCompare.Engine.Reporting.DataComparisonTableSummary object, containing the rows to render</param>
        /// <param name="assignRowIds">a System.Boolean that is true when each row should get a scriptable id (and, if it has differences, an "Accept all" button) — only ever true for the "differing" table, since identical rows have nothing to accept</param>
        /// <returns>returns a string containing the HTML markup for the table</returns>
        private static string BuildTable(IReadOnlyList<DataComparisonTableSummary> rows, bool assignRowIds)
        {
            var sb = new StringBuilder();
            sb.Append("<table class=\"data-rows\">\n<thead><tr>");
            sb.Append("<th>Table</th><th>Source rows</th><th>Target rows</th><th>Matched</th>");
            sb.Append("<th>Changed</th><th>Missing → Target</th><th>Missing → Source</th><th>Reassigned key</th>");
            sb.Append("<th>% Differs</th><th>Accepted</th><th></th>");
            sb.Append("</tr></thead>\n<tbody>\n");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowId = assignRowIds ? $"row-{i}" : null;
                var idAttribute = rowId is null ? string.Empty : $" id=\"{rowId}\"";
                var rowClass = row.HasDifferences ? " class=\"differs\"" : string.Empty;
                sb.Append($"<tr{idAttribute}{rowClass}>");
                sb.Append($"<td>{Encode(row.TableName)}</td>");
                sb.Append($"<td>{row.SourceRowCount:N0}</td>");
                sb.Append($"<td>{row.TargetRowCount:N0}</td>");
                sb.Append($"<td>{row.MatchedCount:N0}</td>");
                sb.Append(BuildCountCell(row.ChangedCount, "changed"));
                sb.Append(BuildCountCell(row.MissingFromTargetCount, "missing-target"));
                sb.Append(BuildCountCell(row.MissingFromSourceCount, "missing-source"));
                sb.Append(BuildCountCell(row.ReassignedKeyCount, "reassigned"));
                sb.Append($"<td>{row.PercentDiffers:0.0}%</td>");
                sb.Append("<td data-category=\"accepted\" data-value=\"0\">0</td>");
                sb.Append("<td>");
                if (rowId is not null && row.HasDifferences)
                {
                    sb.Append($"<button type=\"button\" class=\"accept-all-btn\" onclick=\"acceptAllInTable('{rowId}')\">Accept all</button>");
                }

                sb.Append("</td>");
                sb.Append("</tr>\n");

                // Row-level drill-down (row examples, changed columns) in a full-width row right below
                // the summary row — the same detail the on-screen grid shows when you select a table,
                // now in the export too (previously the export only ever showed the summary counts).
                if (row.Detail is { Children.Count: > 0 } detail)
                {
                    var detailIdAttribute = rowId is null ? string.Empty : $" id=\"{rowId}-detail\"";
                    sb.Append($"<tr class=\"detail-row\"{detailIdAttribute}><td colspan=\"11\">");
                    sb.Append("<details><summary>Show row-level detail</summary>");
                    sb.Append(RenderDetailNodes(detail.Children, rowId, category: null));
                    sb.Append("</details></td></tr>\n");
                }
            }

            sb.Append("</tbody>\n</table>\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// builds a single table cell for a count value, flagging non-zero counts with the "nonzero" CSS
        /// class and carrying both the formatted display text and the raw integer (via <c>data-value</c>)
        /// so the report's Accept feature never has to parse a comma-formatted number back into an integer.
        /// </summary>
        /// <param name="count">a long containing the count value to render</param>
        /// <param name="category">a string holding the <c>data-category</c> slug this cell should be scriptable under</param>
        /// <returns>returns a string containing the HTML markup for the table cell</returns>
        private static string BuildCountCell(long count, string category)
        {
            var cssClass = count > 0 ? " class=\"nonzero\"" : string.Empty;
            return $"<td{cssClass} data-category=\"{category}\" data-value=\"{count}\">{count:N0}</td>";
        }

        /// <summary>
        /// maps a data-difference category to the <c>data-category</c> slug used by its matching summary
        /// count cell — not name-symmetric (a row only in Source is missing when you look at the Target
        /// side, and vice versa), so this is the one place that mapping is encoded.
        /// </summary>
        /// <param name="category">a DataCompare.Engine.Reporting.DataComparisonRowCategory to map</param>
        /// <returns>returns a System.String containing the matching <c>data-category</c> slug</returns>
        private static string CategorySlug(DataComparisonRowCategory category) => category switch
        {
            DataComparisonRowCategory.OnlyInSource => "missing-target",
            DataComparisonRowCategory.OnlyInTarget => "missing-source",
            DataComparisonRowCategory.ReassignedKey => "reassigned",
            DataComparisonRowCategory.Changed => "changed",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        /// <summary>
        /// Recursively renders a table's row-level detail tree as nested lists — a node with children
        /// (e.g. "Rows with changed values (2)") renders as its own collapsible &lt;details&gt; group; a
        /// node with a comparison grid (a reassigned-key row example) renders that grid instead, preceded
        /// by an "Accept" button when a category is in scope for it; a plain leaf node (e.g. one row's
        /// changed-column text) renders as a plain list item.
        /// </summary>
        /// <param name="nodes">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Reporting.DataComparisonDetailNode to render</param>
        /// <param name="rowId">a nullable System.String holding the ancestor table summary row's scriptable id, or null when this table has no accept-able differences (identical tables, or the hash-fallback path)</param>
        /// <param name="category">a nullable DataCompare.Engine.Reporting.DataComparisonRowCategory identifying which count column examples under this subtree belong to — set once when a category container node is encountered, and inherited by its descendants</param>
        /// <returns>returns a System.String containing the HTML markup for this level of the detail tree</returns>
        private static string RenderDetailNodes(IReadOnlyList<DataComparisonDetailNode> nodes, string? rowId, DataComparisonRowCategory? category)
        {
            var sb = new StringBuilder();
            sb.Append("<ul class=\"detail-tree\">\n");
            foreach (var node in nodes)
            {
                var effectiveCategory = node.Category ?? category;
                if (node.GridColumns.Count > 0)
                {
                    sb.Append("<li>");
                    if (rowId is not null && effectiveCategory is not null)
                    {
                        var slug = CategorySlug(effectiveCategory.Value);
                        sb.Append($"<button type=\"button\" class=\"accept-btn\" onclick=\"acceptOne(this, '{rowId}', '{slug}')\">Accept</button> ");
                    }

                    sb.Append(Encode(node.Text));
                    sb.Append(RenderComparisonGrid(node.GridColumns));
                    sb.Append("</li>\n");
                }
                else if (node.Children.Count > 0)
                {
                    sb.Append("<li><details><summary>").Append(Encode(node.Text)).Append("</summary>");
                    sb.Append(RenderDetailNodes(node.Children, rowId, effectiveCategory));
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
                {{ReportBannerBuilder.Css}}
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
                table.data-rows td:last-child { text-align: center; }
                button.accept-btn, button.accept-all-btn { font-size: 11px; padding: 2px 8px; cursor: pointer;
                    border: 1px solid #BBB; border-radius: 3px; background: #FAFAFA; }
                button.accept-btn:disabled, button.accept-all-btn:disabled { opacity: 0.5; cursor: default; }
                span.accepted-badge { color: #2E8B2E; font-weight: 600; font-size: 12px; margin-left: 6px; }
                li.accepted table.comparison-grid td.key-cell,
                li.accepted table.comparison-grid td.match-cell,
                li.accepted table.comparison-grid td.diff-cell { background: #EEEEEE !important; color: #888 !important; font-weight: normal !important; }
            </style>
            </head>
            <body>
            {{body}}
            <script>
                // Temporary, in-browser-only "Accept" for reported differences — deliberately not
                // persisted anywhere (no localStorage, no file write-back). Reopening or regenerating
                // this report resets everything, by design: this tool gets rerun across many refactor
                // iterations of the same migration, and persisting acceptance across runs would risk a
                // real regression silently sneaking through because it happens to match an old accepted
                // signature.
                var DIFF_CATEGORIES = ['changed', 'missing-target', 'missing-source', 'reassigned'];

                function formatCount(n) {
                    var digits = String(n);
                    var result = '';
                    for (var i = 0; i < digits.length; i++) {
                        if (i > 0 && (digits.length - i) % 3 === 0) { result += ','; }
                        result += digits[i];
                    }
                    return result;
                }

                function getValue(cell) { return parseInt(cell.getAttribute('data-value'), 10); }

                function setValue(cell, value) {
                    cell.setAttribute('data-value', value);
                    cell.textContent = formatCount(value);
                    cell.classList.toggle('nonzero', value > 0);
                }

                function adjustHeaderCount(spanId, delta) {
                    var span = document.getElementById(spanId);
                    if (!span) { return; }
                    span.textContent = formatCount(parseInt(span.textContent.replace(/,/g, ''), 10) + delta);
                }

                // Flips a table's row out of "differs" styling, and moves it from "Tables with
                // differences" to "Identical tables" in the header counts, the moment its outstanding
                // differences all reach zero. The row itself stays where it is rather than being
                // physically moved between the two report sections (the "Identical tables" section may
                // not even exist yet if every table originally differed).
                function checkFullyAccepted(row) {
                    var total = 0;
                    DIFF_CATEGORIES.forEach(function (category) {
                        total += getValue(row.querySelector('td[data-category="' + category + '"]'));
                    });

                    if (total === 0 && row.classList.contains('differs')) {
                        row.classList.remove('differs');
                        adjustHeaderCount('differences-count', -1);
                        adjustHeaderCount('identical-count', 1);
                    }
                }

                function acceptOne(button, rowId, category) {
                    if (button.disabled) { return; }

                    var row = document.getElementById(rowId);
                    var diffCell = row.querySelector('td[data-category="' + category + '"]');
                    var acceptedCell = row.querySelector('td[data-category="accepted"]');
                    setValue(diffCell, Math.max(0, getValue(diffCell) - 1));
                    setValue(acceptedCell, getValue(acceptedCell) + 1);

                    button.disabled = true;
                    var item = button.closest('li');
                    item.classList.add('accepted');
                    var badge = document.createElement('span');
                    badge.className = 'accepted-badge';
                    badge.textContent = '✓ Accepted';
                    button.insertAdjacentElement('afterend', badge);

                    checkFullyAccepted(row);
                }

                // Accepts every still-live example rendered for this table, then folds whatever's left
                // in the diff-count cells straight into "Accepted" — the report only ever renders a
                // capped number of examples per category, so anything left after every rendered example
                // is accepted is exactly the un-rendered overflow, with no need to parse the
                // "... N more not shown." text to find out how much that is.
                function acceptAllInTable(rowId) {
                    var detail = document.getElementById(rowId + '-detail');
                    if (detail) {
                        detail.querySelectorAll('.accept-btn:not(:disabled)').forEach(function (button) {
                            button.click();
                        });
                    }

                    var row = document.getElementById(rowId);
                    var acceptedCell = row.querySelector('td[data-category="accepted"]');
                    var remainder = 0;
                    DIFF_CATEGORIES.forEach(function (category) {
                        var cell = row.querySelector('td[data-category="' + category + '"]');
                        remainder += getValue(cell);
                        setValue(cell, 0);
                    });
                    setValue(acceptedCell, getValue(acceptedCell) + remainder);

                    checkFullyAccepted(row);
                }
            </script>
            </body>
            </html>
            """;
    }
}
