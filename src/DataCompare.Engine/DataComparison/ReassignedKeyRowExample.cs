namespace DataCompare.Engine.DataComparison
{

    /// <summary>One row found on both sides with identical non-key values but a different key — the
    /// same row content reassigned to a new key (see <see
    /// cref="KeyedTableDiffResult.ReconcileReassignedKeys"/>), rather than a genuine delete-plus-insert.
    /// Since the non-key values are identical by construction, one shared <see cref="Values"/> holds
    /// them rather than separate source/target dictionaries.</summary>
    /// <param name="SourceKeyValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's key column names and values as read from the source database</param>
    /// <param name="TargetKeyValues">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's key column names and values as read from the target database</param>
    /// <param name="Values">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Object holding the row's non-key column names and values, identical on both sides</param>
    public sealed record ReassignedKeyRowExample(
        IReadOnlyDictionary<string, object?> SourceKeyValues,
        IReadOnlyDictionary<string, object?> TargetKeyValues,
        IReadOnlyDictionary<string, object?> Values);
}
