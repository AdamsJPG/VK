using System.Net;
using System.Text;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Reporting;

/// <summary>
/// Generates a self-contained HTML schema comparison report (inline CSS, no external assets) —
/// the same grouping and DDL diff shown on screen, exportable to a single file.
/// </summary>
public static class SchemaHtmlReportWriter
{
    public static string Generate(
        string sourceLabel,
        string targetLabel,
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
        body.Append(BuildHeader(sourceLabel, targetLabel, result));

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

    private static string BuildHeader(string sourceLabel, string targetLabel, SchemaDiffResult result) => $"""
        <h1>VK Schema Comparison</h1>
        <div class="summary">
            <div><span class="tag source">Source</span> {Encode(sourceLabel)}</div>
            <div><span class="tag target">Target</span> {Encode(targetLabel)}</div>
            <p>{result.TablesOnlyInSource.Count} table(s) only in source, {result.TablesOnlyInTarget.Count} only in target,
               {result.TableDiffs.Count} table(s) with column differences.</p>
        </div>

        """;

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

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

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
            .summary { color: #444; margin-bottom: 8px; }
            .tag { display: inline-block; padding: 2px 8px; border-radius: 3px; color: white; font-weight: bold; font-size: 12px; }
            .tag.source { background: #888; }
            .tag.target { background: #E8792A; }
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
