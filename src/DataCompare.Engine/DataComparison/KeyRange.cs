namespace DataCompare.Engine.DataComparison;

/// <summary>
/// One (exclusive-lower, inclusive-upper] slice of a table's leading key column, used to split a
/// very large table's comparison across several concurrent <see cref="KeyedTableComparer"/> workers
/// (planning.md §19) instead of running the whole table through a single merge-join. Either bound
/// may be null so the first and last chunk can be open-ended.
/// </summary>
public sealed record KeyRange(string ColumnName, object? LowerExclusive, object? UpperInclusive);
