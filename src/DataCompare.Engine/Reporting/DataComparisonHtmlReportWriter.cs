using System.Net;
using System.Text;

namespace DataCompare.Engine.Reporting;

/// <summary>
/// Generates a self-contained HTML data-comparison report (inline CSS, no external assets) — the
/// same rollup grouping and unmissable difference styling shown on screen, exportable to a single
/// file. A sibling to <see cref="SchemaHtmlReportWriter"/>, not a mode of it: schema and data are
/// answering two different questions (does the table structure match vs. does the data match), so
/// they get separate reports rather than one export button silently only ever covering one of them.
/// </summary>
public static class DataComparisonHtmlReportWriter
{
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

    private static string BuildTable(IReadOnlyList<DataComparisonTableSummary> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table class=\"data-rows\">\n<thead><tr>");
        sb.Append("<th>Table</th><th>Source rows</th><th>Target rows</th><th>Matched</th>");
        sb.Append("<th>Changed</th><th>Missing → Target</th><th>Missing → Source</th>");
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
            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n\n");
        return sb.ToString();
    }

    private static string BuildCountCell(long count) =>
        count > 0 ? $"<td class=\"nonzero\">{count:N0}</td>" : $"<td>{count:N0}</td>";

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

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
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;
}
