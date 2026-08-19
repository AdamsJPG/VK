namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Renders a column definition as an approximate T-SQL DDL line, for the results screen's
    /// side-by-side "what does this table look like" view. Not intended to be executable DDL —
    /// good enough for a human to see what a column is, not a script generator.
    /// </summary>
    public static class TableDdlGenerator
    {
        /// <summary>
        /// Formats one column as a single DDL-style line, e.g. "[Name] [varchar](50) NOT NULL".
        /// </summary>
        /// <param name="column">a DataCompare.Engine.Schema.ColumnSchema describing the column to format</param>
        /// <returns>returns a System.String containing the formatted column line</returns>
        public static string FormatColumnLine(ColumnSchema column) =>
            $"[{column.Name}] {FormatType(column)} {(column.IsNullable ? "NULL" : "NOT NULL")}";

        /// <summary>
        /// Formats a column's data type, including its length/precision suffix where applicable.
        /// </summary>
        /// <param name="column">a DataCompare.Engine.Schema.ColumnSchema describing the column whose type to format</param>
        /// <returns>returns a System.String containing the formatted type, e.g. "[nvarchar](50)" or "[decimal](19, 6)"</returns>
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

        /// <summary>
        /// Formats a column's max length for display, converting a Unicode character count back from
        /// the byte count SQL Server reports, or "MAX" for MAX-length columns.
        /// </summary>
        /// <param name="maxLength">a System.Int16 holding the column's max length in bytes, or -1 for a MAX-length column</param>
        /// <param name="unicodeDivisor">a System.Int32 divisor applied to convert byte length to character length (2 for Unicode types, 1 otherwise)</param>
        /// <returns>returns a System.String containing "MAX" or the formatted character length</returns>
        private static string FormatLength(short maxLength, int unicodeDivisor) =>
            maxLength == -1 ? "MAX" : (maxLength / unicodeDivisor).ToString();
    }
}
