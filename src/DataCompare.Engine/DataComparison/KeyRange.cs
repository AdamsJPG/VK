namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// One (exclusive-lower, inclusive-upper] slice of a table's leading key column, used to split a
    /// very large table's comparison across several concurrent <see cref="KeyedTableComparer"/> workers
    /// (planning.md §19) instead of running the whole table through a single merge-join. Either bound
    /// may be null so the first and last chunk can be open-ended.
    /// </summary>
    /// <param name="ColumnName">a System.String holding the name of the table's leading key column that this range slices on</param>
    /// <param name="LowerExclusive">a System.Object holding the exclusive lower bound of the range, or null when this is the first chunk with no lower bound</param>
    /// <param name="UpperInclusive">a System.Object holding the inclusive upper bound of the range, or null when this is the last chunk with no upper bound</param>
    public sealed record KeyRange(string ColumnName, object? LowerExclusive, object? UpperInclusive);
}
