namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The full schema of a database — every table (and, when requested, view/function/stored
    /// procedure) read from it, as captured for comparison.
    /// </summary>
    /// <param name="Tables">an IReadOnlyList where T is a DataCompare.Engine.Schema.TableSchema object, containing every table read from the database</param>
    public sealed record DatabaseSchema(IReadOnlyList<TableSchema> Tables)
    {
        // Not positional constructor parameters: a collection type's only valid positional default is
        // null (collection expressions like [] aren't compile-time constants), which would force every
        // caller to either pass empty collections explicitly or deal with a nullable property. A plain
        // property initializer has no such restriction.

        /// <summary>every view read from the database, empty unless views were requested.</summary>
        public IReadOnlyList<TableSchema> Views { get; init; } = [];

        /// <summary>every function and stored procedure read from the database, empty unless requested.</summary>
        public IReadOnlyList<RoutineSchema> Routines { get; init; } = [];
    }
}
