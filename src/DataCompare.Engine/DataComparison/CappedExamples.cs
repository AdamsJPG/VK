namespace DataCompare.Engine.DataComparison;

/// <summary>
/// A running total plus a bounded set of examples — lets a huge table report an exact count
/// without holding every matching row in memory (see planning.md's "no silent caps" principle:
/// <see cref="TotalCount"/> is always exact, only <see cref="Examples"/> is capped).
/// </summary>
public sealed record CappedExamples<T>(IReadOnlyList<T> Examples, long TotalCount);
