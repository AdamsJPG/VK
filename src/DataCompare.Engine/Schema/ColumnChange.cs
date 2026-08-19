namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// A single column whose definition differs between the source and target schemas.
    /// </summary>
    /// <param name="ColumnName">a string containing the name of the column that differs</param>
    /// <param name="Source">a DataCompare.Engine.Schema.ColumnSchema object describing the column as defined in the source database</param>
    /// <param name="Target">a DataCompare.Engine.Schema.ColumnSchema object describing the column as defined in the target database</param>
    public sealed record ColumnChange(string ColumnName, ColumnSchema Source, ColumnSchema Target);
}
