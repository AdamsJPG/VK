namespace DataCompare.Engine.Schema
{

    /// <summary>One line of a side-by-side routine/view definition-text diff. <see cref="IsPresent"/>
    /// is false for a blank spacer line (keeps the two sides aligned when one side has more lines than
    /// the other).</summary>
    /// <param name="Text">a System.String of the rendered definition text for this line</param>
    /// <param name="IsHighlighted">a System.Boolean indicating whether this line should be highlighted as changed</param>
    /// <param name="IsPresent">a System.Boolean indicating whether this line represents real content, defaulting to true; false marks a blank alignment spacer</param>
    public sealed record DefinitionDiffLine(string Text, bool IsHighlighted, bool IsPresent = true)
    {
        /// <summary>
        /// creates a blank spacer line used to keep the source and target sides aligned when one side
        /// has more lines than the other.
        /// </summary>
        /// <returns>returns a DataCompare.Engine.Schema.DefinitionDiffLine object with empty text, no highlight, and IsPresent set to false</returns>
        public static DefinitionDiffLine Blank() => new(string.Empty, IsHighlighted: false, IsPresent: false);
    }
}
