namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The full schema of a database — every table read from it, as captured for comparison.
    /// </summary>
    /// <param name="Tables">an IReadOnlyList where T is a DataCompare.Engine.Schema.TableSchema object, containing every table read from the database</param>
    public sealed record DatabaseSchema(IReadOnlyList<TableSchema> Tables);
}
