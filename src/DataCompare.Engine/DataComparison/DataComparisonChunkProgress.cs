namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Reports that one key-range chunk of a partitioned large table's comparison (planning.md §19)
    /// has finished, identifying the chunk by its position within the table's plan (see <see
    /// cref="DataComparisonTablePlan.Chunks"/>) so a caller can update the matching row without the
    /// orchestrator needing to know anything about how that row is represented.
    /// </summary>
    /// <param name="TableIndex">an System.Int32 holding the completed chunk's table's position in the overall comparison plan</param>
    /// <param name="ChunkIndex">an System.Int32 holding the completed chunk's position within its table's <see cref="DataComparisonTablePlan.Chunks"/> list</param>
    /// <param name="CompletedChunksForTable">an System.Int32 holding the number of this table's chunks completed so far, including this one</param>
    /// <param name="TotalChunksForTable">an System.Int32 holding the total number of chunks this table was split into</param>
    public sealed record DataComparisonChunkProgress(
        int TableIndex, int ChunkIndex, int CompletedChunksForTable, int TotalChunksForTable);
}
