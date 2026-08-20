using System.Windows.Media;
using DataCompare.Engine.Reporting;

namespace DataCompare.App.ViewModels
{

    /// <summary>One cell of a reassigned-key or changed-row comparison grid, shown in the TreeView's
    /// <see cref="DiffTreeNode.GridColumns"/> — the source and target sides each contribute one cell per
    /// column, laid out as two column-groups side by side in a single row (source group on the left,
    /// target group on the right), not stacked as separate rows.</summary>
    /// <param name="Header">a System.String holding the column name shown above this cell</param>
    /// <param name="ValueDisplay">a System.String holding this cell's formatted value</param>
    /// <param name="CellKind">a DataCompare.Engine.Reporting.DataComparisonGridCellKind describing how this cell should be highlighted</param>
    public sealed record GridColumnCell(string Header, string ValueDisplay, DataComparisonGridCellKind CellKind)
    {
        private static readonly Brush MatchedBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEA));
        private static readonly Brush ExpectedDifferenceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD));
        private static readonly Brush RealDifferenceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0xE6));

        /// <summary>the background this cell is shown with — soft green for a matched value, soft
        /// yellow for an expected/ignorable difference (e.g. a reassigned key), soft red for a real
        /// difference (e.g. an actually-changed value).</summary>
        public Brush CellBackground => CellKind switch
        {
            DataComparisonGridCellKind.ExpectedDifference => ExpectedDifferenceBrush,
            DataComparisonGridCellKind.RealDifference => RealDifferenceBrush,
            _ => MatchedBrush,
        };
    }
}
