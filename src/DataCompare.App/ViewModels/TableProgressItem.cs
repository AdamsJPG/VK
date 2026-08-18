using CommunityToolkit.Mvvm.ComponentModel;

namespace DataCompare.App.ViewModels;

/// <summary>One row in the data-comparison progress popup — a table name plus whether it's been scanned yet.</summary>
public partial class TableProgressItem(string tableName) : ObservableObject
{
    public string TableName { get; } = tableName;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string _summary = string.Empty;
}
