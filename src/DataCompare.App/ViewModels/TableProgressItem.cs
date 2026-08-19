using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DataCompare.App.ViewModels;

/// <summary>One row in the data-comparison progress popup — a table name plus whether it's been
/// scanned yet. A partitioned large table (planning.md §19) additionally carries one <see
/// cref="ChunkProgressItem"/> per key-range chunk; a normal table's <see cref="Chunks"/> stays empty,
/// which renders identically to a plain row (no expander shown).</summary>
public partial class TableProgressItem(string tableName) : ObservableObject
{
    public string TableName { get; } = tableName;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>True once this table's comparison finds at least one difference — bound to a
    /// deliberately unmissable (red/bold/flashing) style in the progress window, since silently
    /// discovering a real difference in this exercise is the one outcome that must never go
    /// unnoticed.</summary>
    [ObservableProperty]
    private bool _hasDifferences;

    public ObservableCollection<ChunkProgressItem> Chunks { get; } = [];
}
