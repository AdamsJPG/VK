using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DataCompare.App.ViewModels
{

    /// <summary>Presentation node for the schema/data diff TreeViews — one flat model for all node kinds.</summary>
    /// <param name="text">a System.String to display for this node</param>
    public sealed class DiffTreeNode(string text)
    {
        /// <summary>the text shown for this node.</summary>
        public string Text { get; } = text;

        /// <summary>this node's child nodes, if any.</summary>
        public ObservableCollection<DiffTreeNode> Children { get; } = [];

        /// <summary>the source-side cells of this node's comparison grid, if it carries one — empty
        /// for every other kind of node. Always the same length as <see cref="TargetGridColumns"/>.</summary>
        public ObservableCollection<GridColumnCell> SourceGridColumns { get; } = [];

        /// <summary>the target-side cells of this node's comparison grid, if it carries one — empty
        /// for every other kind of node. Always the same length as <see cref="SourceGridColumns"/>.</summary>
        public ObservableCollection<GridColumnCell> TargetGridColumns { get; } = [];

        /// <summary>Button caption shown next to <see cref="Text"/> when <see cref="ActionCommand"/> is
        /// set; null for a plain, non-interactive node (the common case).</summary>
        public string? ActionLabel { get; init; }

        /// <summary>Optional command for an on-demand drill-down action (e.g. opening a large-content
        /// column's real value) that only some nodes offer; null for a plain node.</summary>
        public ICommand? ActionCommand { get; init; }
    }
}
