namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// A running total plus a bounded set of examples — lets a huge table report an exact count
    /// without holding every matching row in memory (see planning.md's "no silent caps" principle:
    /// <see cref="TotalCount"/> is always exact, only <see cref="Examples"/> is capped).
    /// </summary>
    /// <typeparam name="T">the type of example item captured for this category</typeparam>
    /// <param name="Examples">a System.Collections.Generic.IReadOnlyList of T holding the capped set of example items retained for display</param>
    /// <param name="TotalCount">a System.Int64 holding the exact total count of matching items, independent of how many examples were kept</param>
    public sealed record CappedExamples<T>(IReadOnlyList<T> Examples, long TotalCount);
}
