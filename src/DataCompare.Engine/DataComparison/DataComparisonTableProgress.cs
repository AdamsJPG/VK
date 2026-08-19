namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Reports that one table's comparison has finished, whether compared as a single unit or as the
    /// last chunk of a partitioned large table combining into a final result (planning.md §19).
    /// </summary>
    /// <param name="TableIndex">an System.Int32 holding the completed table's position in the overall comparison plan</param>
    /// <param name="HasDifferences">a System.Boolean that is true when the completed table has any changed, added, or missing rows</param>
    /// <param name="CompletedTableCount">an System.Int32 holding the number of tables completed so far, including this one</param>
    /// <param name="TotalTableCount">an System.Int32 holding the total number of tables in this comparison run</param>
    public sealed record DataComparisonTableProgress(
        int TableIndex, bool HasDifferences, int CompletedTableCount, int TotalTableCount);
}
