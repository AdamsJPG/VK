using System.Collections.ObjectModel;

namespace DataCompare.App.ViewModels;

/// <summary>Presentation node for the schema/data diff TreeViews — one flat model for all node kinds.</summary>
public sealed class DiffTreeNode(string text)
{
    public string Text { get; } = text;
    public ObservableCollection<DiffTreeNode> Children { get; } = [];
}
