namespace DataCompare.Engine.DataComparison;

/// <summary>One row whose key matched on both sides but whose non-key values differ.</summary>
public sealed record ChangedRowExample(
    IReadOnlyDictionary<string, object?> KeyValues,
    IReadOnlyList<string> ChangedColumnNames,
    IReadOnlyDictionary<string, object?> SourceValues,
    IReadOnlyDictionary<string, object?> TargetValues);
