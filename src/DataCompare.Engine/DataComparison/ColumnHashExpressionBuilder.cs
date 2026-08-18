using DataCompare.Engine.Schema;

namespace DataCompare.Engine.DataComparison;

/// <summary>
/// Builds the SQL that computes a per-row content hash server-side, so only a 32-byte hash (not
/// full row content) travels back to the app. See planning.md §6 for the overall algorithm.
/// </summary>
internal static class ColumnHashExpressionBuilder
{
    // Unlikely to collide with real data; used so CONCAT_WS never silently drops a NULL argument
    // (it does for actual NULLs, which would otherwise make differently-shaped NULL rows collide).
    private const string NullSentinel = "␀NULL␀";

    public static string BuildGroupedCountQuery(TableSchema table, IReadOnlyList<string> columnNames)
    {
        var hashExpression = BuildRowHashExpression(table, columnNames);
        return $"""
            SELECT RowHash, COUNT_BIG(*) AS OccurrenceCount
            FROM (
                SELECT {hashExpression} AS RowHash
                FROM {SqlIdentifier.QuoteTable(table)}
            ) AS RowHashes
            GROUP BY RowHash;
            """;
    }

    public static string BuildSampleRowsQuery(TableSchema table, IReadOnlyList<string> columnNames)
    {
        var hashExpression = BuildRowHashExpression(table, columnNames);
        return $"""
            SELECT TOP (@SampleSize) *
            FROM {SqlIdentifier.QuoteTable(table)}
            WHERE {hashExpression} = @Hash;
            """;
    }

    private static string BuildRowHashExpression(TableSchema table, IReadOnlyList<string> columnNames)
    {
        var columnsByName = table.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var parts = columnNames.Select(name => BuildColumnTextExpression(columnsByName[name]));
        return $"HASHBYTES('SHA2_256', CONCAT_WS(N'|', {string.Join(", ", parts)}))";
    }

    private static string BuildColumnTextExpression(ColumnSchema column)
    {
        var quotedName = SqlIdentifier.Quote(column.Name);

        // Explicit CONVERT styles avoid locale-dependent default formatting and keep binary/float
        // conversions lossless, so identical values hash identically regardless of server settings.
        var convertExpression = column.DataType.ToLowerInvariant() switch
        {
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" =>
                $"CONVERT(nvarchar(max), {quotedName}, 1)",
            "float" or "real" =>
                $"CONVERT(nvarchar(max), {quotedName}, 2)",
            "datetimeoffset" =>
                $"CONVERT(nvarchar(max), {quotedName}, 127)",
            "datetime" or "smalldatetime" or "datetime2" =>
                $"CONVERT(nvarchar(max), {quotedName}, 121)",
            "date" =>
                $"CONVERT(nvarchar(max), {quotedName}, 23)",
            "time" =>
                $"CONVERT(nvarchar(max), {quotedName}, 114)",
            _ => $"CONVERT(nvarchar(max), {quotedName})",
        };

        return $"ISNULL({convertExpression}, N'{NullSentinel}')";
    }
}
