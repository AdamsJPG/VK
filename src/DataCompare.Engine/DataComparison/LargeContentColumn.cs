using DataCompare.Engine.Schema;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Identifies MAX-length binary/text columns (and <c>xml</c>, which SQL Server always reports as
    /// MaxLength -1) that are expensive to pull and compare in full — <see cref="KeyedTableComparer"/>
    /// compares these via a small server-side hash instead of the raw value (planning.md §18, §19), so a
    /// very large table with a wide report/blob/XML column doesn't have to transfer that column's full
    /// content for every row just to detect whether it changed.
    /// </summary>
    public static class LargeContentColumn
    {
        /// <summary>
        /// Determines whether a column is a MAX-length binary/text type, or <c>xml</c>.
        /// </summary>
        /// <param name="column">a DataCompare.Engine.Schema.ColumnSchema describing the column to check</param>
        /// <returns>returns a System.Boolean that is true when the column is varbinary(max), nvarchar(max), varchar(max), or xml</returns>
        public static bool Is(ColumnSchema column) =>
            column.MaxLength == -1
            && column.DataType.ToLowerInvariant() is "varbinary" or "nvarchar" or "varchar" or "xml";
    }
}
