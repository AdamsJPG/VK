namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// One key-range chunk of a partitioned large table's planned comparison (planning.md §19) — a
    /// plain Engine-layer counterpart to the App layer's chunk progress row, carrying only the display
    /// label a caller needs before any chunk has actually run.
    /// </summary>
    /// <param name="Label">a System.String label identifying this chunk (e.g. its key range)</param>
    public sealed record DataComparisonChunkPlan(string Label);
}
