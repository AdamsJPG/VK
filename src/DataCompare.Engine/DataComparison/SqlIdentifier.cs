using DataCompare.Engine.Schema;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Internal helper for safely bracket-quoting SQL Server identifiers when building dynamically
    /// generated T-SQL for the comparison engine.
    /// </summary>
    internal static class SqlIdentifier
    {
        /// <summary>
        /// Bracket-quotes a single SQL Server identifier, escaping any embedded closing brackets.
        /// </summary>
        /// <param name="name">a System.String holding the raw identifier to quote</param>
        /// <returns>returns a System.String holding the identifier wrapped in square brackets, e.g. <c>[Name]</c></returns>
        public static string Quote(string name) => $"[{name.Replace("]", "]]")}]";

        /// <summary>
        /// Bracket-quotes a table's schema and table name together as a single schema-qualified identifier.
        /// </summary>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to quote</param>
        /// <returns>returns a System.String holding the schema-qualified, bracket-quoted table reference, e.g. <c>[dbo].[Orders]</c></returns>
        public static string QuoteTable(TableSchema table) => $"{Quote(table.SchemaName)}.{Quote(table.TableName)}";
    }
}
