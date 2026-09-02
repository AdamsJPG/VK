namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// A single function or stored procedure whose definition text differs between the source and
    /// target databases.
    /// </summary>
    /// <param name="RoutineName">a System.String schema-qualified name of the routine that differs</param>
    /// <param name="Kind">a DataCompare.Engine.Schema.RoutineKind indicating whether this is a function or a stored procedure</param>
    public sealed record RoutineDiff(string RoutineName, RoutineKind Kind);
}
