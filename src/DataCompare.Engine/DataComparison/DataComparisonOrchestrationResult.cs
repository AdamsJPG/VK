using DataCompare.Engine.Reporting;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// The outcome of one full <see cref="DataComparisonOrchestrator"/> run: a row-count summary (with
    /// row-level drill-down detail where applicable) for every table common to both sides.
    /// </summary>
    /// <param name="Rows">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Reporting.DataComparisonTableSummary holding one entry per compared table, in table-plan order</param>
    /// <param name="ComparedTableCount">an System.Int32 holding the total number of tables that exist on both sides and were compared</param>
    /// <param name="DifferingTableCount">an System.Int32 holding the number of compared tables with at least one difference</param>
    public sealed record DataComparisonOrchestrationResult(
        IReadOnlyList<DataComparisonTableSummary> Rows, int ComparedTableCount, int DifferingTableCount);
}
