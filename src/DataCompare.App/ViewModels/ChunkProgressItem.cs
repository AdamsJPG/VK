using CommunityToolkit.Mvvm.ComponentModel;

namespace DataCompare.App.ViewModels
{

    /// <summary>One key-range chunk of a partitioned large table's comparison (planning.md §19) — shown
    /// as a child row under its <see cref="TableProgressItem"/> so a long-running big table's progress
    /// isn't a single opaque spinner for its entire duration.</summary>
    /// <param name="label">a System.String label identifying this chunk (e.g. its key range), shown as its row text</param>
    public partial class ChunkProgressItem(string label) : ObservableObject
    {
        /// <summary>the display label for this chunk.</summary>
        public string Label { get; } = label;

        [ObservableProperty]
        private bool _isComplete;
    }
}
