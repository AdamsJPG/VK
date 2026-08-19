namespace DataCompare.App.ViewModels
{

    /// <summary>One line of the side-by-side DDL detail pane. <see cref="IsPresent"/> is false for a
    /// blank spacer line (keeps the two sides aligned when a column only exists on one side).</summary>
    /// <param name="Text">a System.String holding the line's display text</param>
    /// <param name="IsHighlighted">a System.Boolean that is true when this line should be highlighted as different</param>
    /// <param name="IsPresent">a System.Boolean that is false when this line is a blank alignment spacer, defaulting to true</param>
    public sealed record DdlLine(string Text, bool IsHighlighted, bool IsPresent = true)
    {
        /// <summary>creates a blank spacer line — used to keep both sides' DDL panes aligned when a
        /// column only exists on one side.</summary>
        /// <returns>returns a DataCompare.App.ViewModels.DdlLine representing an empty, non-present spacer line</returns>
        public static DdlLine Blank() => new(string.Empty, IsHighlighted: false, IsPresent: false);
    }
}
