namespace DataCompare.Engine.DataComparison
{

    /// <summary>One row whose key matched on both sides but whose non-key values differ.</summary>
    /// <param name="KeyValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's key column names and values</param>
    /// <param name="ChangedColumnNames">a System.Collections.Generic.IReadOnlyList of System.String holding the names of the non-key columns whose values differ between source and target</param>
    /// <param name="SourceValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's full column values as read from the source database</param>
    /// <param name="TargetValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's full column values as read from the target database</param>
    public sealed record ChangedRowExample(
        IReadOnlyDictionary<string, object?> KeyValues,
        IReadOnlyList<string> ChangedColumnNames,
        IReadOnlyDictionary<string, object?> SourceValues,
        IReadOnlyDictionary<string, object?> TargetValues);
}
