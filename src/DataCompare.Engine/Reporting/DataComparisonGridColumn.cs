namespace DataCompare.Engine.Reporting
{

    /// <summary>One column of a reassigned-key row's comparison grid (see <see
    /// cref="DataComparisonDetailNode.GridColumns"/>) — a display-ready DTO with values already
    /// formatted to strings, since both the WPF app and the HTML report render it directly.</summary>
    /// <param name="ColumnName">a System.String holding the column's name</param>
    /// <param name="SourceValueDisplay">a System.String holding the column's formatted value as read from the source database</param>
    /// <param name="TargetValueDisplay">a System.String holding the column's formatted value as read from the target database</param>
    /// <param name="CellKind">a DataCompare.Engine.Reporting.DataComparisonGridCellKind describing how this column should be highlighted</param>
    public sealed record DataComparisonGridColumn(
        string ColumnName, string SourceValueDisplay, string TargetValueDisplay, DataComparisonGridCellKind CellKind);
}
