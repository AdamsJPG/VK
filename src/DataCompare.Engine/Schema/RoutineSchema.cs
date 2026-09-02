namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The schema definition of a single function or stored procedure — unlike a table or view, a
    /// routine has no columns and no data to compare, only a name and its raw T-SQL definition text.
    /// </summary>
    /// <param name="SchemaName">a System.String name of the SQL Server schema the routine belongs to</param>
    /// <param name="Name">a System.String name of the routine</param>
    /// <param name="ModifiedAt">a System.DateTime of the routine's last modification date</param>
    /// <param name="Definition">a nullable System.String holding the routine's raw T-SQL definition text, or null when SQL Server won't disclose it (e.g. WITH ENCRYPTION)</param>
    /// <param name="Kind">a DataCompare.Engine.Schema.RoutineKind indicating whether this is a function or a stored procedure</param>
    public sealed record RoutineSchema(string SchemaName, string Name, DateTime ModifiedAt, string? Definition, RoutineKind Kind)
    {
        /// <summary>
        /// the schema-qualified name of the routine, formatted as "SchemaName.Name".
        /// </summary>
        public string FullName => $"{SchemaName}.{Name}";
    }
}
