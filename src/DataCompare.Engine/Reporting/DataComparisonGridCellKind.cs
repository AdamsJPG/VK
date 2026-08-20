namespace DataCompare.Engine.Reporting
{

    /// <summary>How one column of a <see cref="DataComparisonGridColumn"/> should be highlighted: whether
    /// its source/target values matching (or not matching) is expected and benign, or an actual
    /// difference worth flagging.</summary>
    public enum DataComparisonGridCellKind
    {
        /// <summary>The source and target values match — shown in a neutral, unremarkable style.</summary>
        Matched,

        /// <summary>The source and target values differ, but that's expected and safe to ignore — e.g.
        /// the key column of a reassigned-key row, where a different key is the whole point of the
        /// category. Shown distinctly from a real difference so it doesn't read as an alarm.</summary>
        ExpectedDifference,

        /// <summary>The source and target values differ, and that's a real, unexpected difference — e.g.
        /// a non-key column's value that changed. Shown loudly, consistent with how a real data
        /// difference is flagged everywhere else in this app.</summary>
        RealDifference,
    }
}
