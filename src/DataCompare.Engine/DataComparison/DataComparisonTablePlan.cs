namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// One table's place in a <see cref="DataComparisonOrchestrator"/> run, reported up front via its
    /// plan progress callback so a caller (e.g. the GUI's progress popup) can render every table's row
    /// immediately instead of only as each one finishes. A table only known to need range-partitioning
    /// after boundary sampling completes starts with an empty <see cref="Chunks"/> list.
    /// </summary>
    /// <param name="TableName">a System.String holding the compared table's fully-qualified name</param>
    /// <param name="Chunks">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.DataComparison.DataComparisonChunkPlan holding this table's key-range chunks, empty for a table compared as a single unit</param>
    public sealed record DataComparisonTablePlan(string TableName, IReadOnlyList<DataComparisonChunkPlan> Chunks);
}
