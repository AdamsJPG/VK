using System.Windows;
using DataCompare.App.ViewModels;

namespace DataCompare.App;

/// <summary>
/// Interaction logic for DataComparisonProgressWindow.xaml
/// </summary>
public partial class DataComparisonProgressWindow : Window
{
    public DataComparisonProgressWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = VkIconFactory.Render(64);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelCompare();
        }
    }
}
