using System.Windows;
using DataCompare.App.ViewModels;

namespace DataCompare.App
{

    /// <summary>
    /// Interaction logic for DataComparisonProgressWindow.xaml
    /// </summary>
    public partial class DataComparisonProgressWindow : Window
    {
        /// <summary>
        /// the ingredients for my class are as follows...
        /// </summary>
        /// <param name="viewModel">a DataCompare.App.ViewModels.MainWindowViewModel to bind this window's progress display to.</param>
        public DataComparisonProgressWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Icon = VkIconFactory.Render(64);
        }

        /// <summary>
        /// handles the Cancel button click by asking the bound view model to cancel the in-progress comparison.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.CancelCompare();
            }
        }
    }
}
