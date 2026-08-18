namespace DataCompare.Engine.Schema;

/// <summary>
/// Renders a column definition as an approximate T-SQL DDL line, for the results screen's
/// side-by-side "what does this table look like" view. Not intended to be executable DDL —
/// good enough for a human to see what a column is, not a script generator.
/// </summary>
public static class TableDdlGenerator
{
    public static string FormatColumnLine(ColumnSchema column) =>
        $"[{column.Name}] {FormatType(column)} {(column.IsNullable ? "NULL" : "NOT NULL")}";

    private static string FormatType(ColumnSchema column)
    {
        var typeName = column.DataType.ToLowerInvariant();
        return typeName switch
        {
            "nvarchar" or "nchar" => $"[{column.DataType}]({FormatLength(column.MaxLength, unicodeDivisor: 2)})",
            "varchar" or "char" or "binary" or "varbinary" => $"[{column.DataType}]({FormatLength(column.MaxLength, unicodeDivisor: 1)})",
            "decimal" or "numeric" => $"[{column.DataType}]({column.Precision}, {column.Scale})",
            _ => $"[{column.DataType}]",
        };
    }

    private static string FormatLength(short maxLength, int unicodeDivisor) =>
        maxLength == -1 ? "MAX" : (maxLength / unicodeDivisor).ToString();
}
