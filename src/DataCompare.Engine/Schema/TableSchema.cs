namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The schema of a single table: its schema/table name, its columns, and when it was last modified.
    /// </summary>
    /// <param name="SchemaName">a System.String name of the SQL Server schema the table belongs to</param>
    /// <param name="TableName">a System.String name of the table</param>
    /// <param name="Columns">an System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.ColumnSchema objects describing the table's columns</param>
    /// <param name="ModifiedAt">a System.DateTime of the table's last modification date, defaulting to System.DateTime.MinValue when not supplied</param>
    public sealed record TableSchema(
        string SchemaName, string TableName, IReadOnlyList<ColumnSchema> Columns, DateTime ModifiedAt = default)
    {
        /// <summary>
        /// the schema-qualified name of the table, formatted as "SchemaName.TableName".
        /// </summary>
        public string FullName => $"{SchemaName}.{TableName}";

        /// <summary>
        /// the table's primary key columns, ordered by their position within the key.
        /// </summary>
        public IReadOnlyList<ColumnSchema> PrimaryKeyColumnsInOrder =>
            Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.PrimaryKeyOrdinal).ToList();
    }
}
