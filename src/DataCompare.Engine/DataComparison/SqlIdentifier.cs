using DataCompare.Engine.Schema;

namespace DataCompare.Engine.DataComparison;

internal static class SqlIdentifier
{
    public static string Quote(string name) => $"[{name.Replace("]", "]]")}]";

    public static string QuoteTable(TableSchema table) => $"{Quote(table.SchemaName)}.{Quote(table.TableName)}";
}
