namespace DataCompare.Engine.DataComparison
{

    /// <summary>A captured example row (key + full value set) for display — not used for matching itself.</summary>
    /// <param name="Values">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's full set of column names and values</param>
    public sealed record RowExample(IReadOnlyDictionary<string, object?> Values);
}
