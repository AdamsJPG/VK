namespace DataCompare.Engine.DataComparison;

/// <summary>A captured example row (key + full value set) for display — not used for matching itself.</summary>
public sealed record RowExample(IReadOnlyDictionary<string, object?> Values);
