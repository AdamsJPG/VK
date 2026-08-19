namespace DataCompare.Engine.Reporting
{

    /// <summary>
    /// One node of a table's row-level comparison detail (e.g. "Rows only in Target (3)" with its own
    /// example rows as children) — a plain Engine-layer tree so the HTML report can render the same
    /// drill-down detail already shown on screen (via the App layer's <c>DiffTreeNode</c>), without the
    /// Engine project depending on the App project's view model type.
    /// </summary>
    /// <param name="Text">a System.String holding this node's display text</param>
    /// <param name="Children">a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Reporting.DataComparisonDetailNode holding this node's child nodes, empty for a leaf</param>
    /// <param name="LargeContentAction">a nullable DataCompare.Engine.Reporting.DataComparisonLargeContentAction describing the on-demand "open both" drill-down available for this node, or null for a node with no such action</param>
    public sealed record DataComparisonDetailNode(
        string Text,
        IReadOnlyList<DataComparisonDetailNode> Children,
        DataComparisonLargeContentAction? LargeContentAction = null);
}
