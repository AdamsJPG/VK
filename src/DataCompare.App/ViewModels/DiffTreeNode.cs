using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DataCompare.App.ViewModels;

/// <summary>Presentation node for the schema/data diff TreeViews — one flat model for all node kinds.</summary>
public sealed class DiffTreeNode(string text)
{
    public string Text { get; } = text;
    public ObservableCollection<DiffTreeNode> Children { get; } = [];

    /// <summary>Button caption shown next to <see cref="Text"/> when <see cref="ActionCommand"/> is
    /// set; null for a plain, non-interactive node (the common case).</summary>
    public string? ActionLabel { get; init; }

    /// <summary>Optional command for an on-demand drill-down action (e.g. opening a large-content
    /// column's real value) that only some nodes offer; null for a plain node.</summary>
    public ICommand? ActionCommand { get; init; }
}
