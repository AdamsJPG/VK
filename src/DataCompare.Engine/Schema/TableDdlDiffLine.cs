namespace DataCompare.Engine.Schema
{

    /// <summary>One line of a side-by-side DDL diff. <see cref="IsPresent"/> is false for a blank
    /// spacer line (keeps the two sides aligned when a column only exists on one side).</summary>
    /// <param name="Text">a System.String of the rendered DDL text for this line</param>
    /// <param name="IsHighlighted">a System.Boolean indicating whether this line should be highlighted as changed</param>
    /// <param name="IsPresent">a System.Boolean indicating whether this line represents real content, defaulting to true; false marks a blank alignment spacer</param>
    public sealed record TableDdlDiffLine(string Text, bool IsHighlighted, bool IsPresent = true)
    {
        /// <summary>
        /// creates a blank spacer line used to keep the source and target sides aligned when a column
        /// exists on only one side.
        /// </summary>
        /// <returns>returns a DataCompare.Engine.Schema.TableDdlDiffLine object with empty text, no highlight, and IsPresent set to false</returns>
        public static TableDdlDiffLine Blank() => new(string.Empty, IsHighlighted: false, IsPresent: false);
    }
}
