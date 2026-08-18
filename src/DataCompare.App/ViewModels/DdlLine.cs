namespace DataCompare.App.ViewModels;

/// <summary>One line of the side-by-side DDL detail pane. <see cref="IsPresent"/> is false for a
/// blank spacer line (keeps the two sides aligned when a column only exists on one side).</summary>
public sealed record DdlLine(string Text, bool IsHighlighted, bool IsPresent = true)
{
    public static DdlLine Blank() => new(string.Empty, IsHighlighted: false, IsPresent: false);
}
