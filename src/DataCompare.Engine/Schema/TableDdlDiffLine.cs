namespace DataCompare.Engine.Schema;

/// <summary>One line of a side-by-side DDL diff. <see cref="IsPresent"/> is false for a blank
/// spacer line (keeps the two sides aligned when a column only exists on one side).</summary>
public sealed record TableDdlDiffLine(string Text, bool IsHighlighted, bool IsPresent = true)
{
    public static TableDdlDiffLine Blank() => new(string.Empty, IsHighlighted: false, IsPresent: false);
}
