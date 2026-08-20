using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DataCompare.App.ViewModels;
using Microsoft.Win32;

namespace DataCompare.App
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// the ingredients for my class are as follows...
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Icon = VkIconFactory.Render(64);
            Loaded += MainWindow_Loaded;
        }

        // Restores the last-used server/database/user once the visual tree is realized — the
        // PasswordBoxes aren't reachable via FindPasswordBox until the ContentControls have applied
        // their templates, which isn't guaranteed yet in the constructor.
        /// <summary>
        /// restores the last-used connection profile and remembered passwords once the window's visual
        /// tree has been realized.
        /// </summary>
        /// <param name="sender">a System.Object representing the window that raised the Loaded event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the Loaded event.</param>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;
            await viewModel.LoadLastUsedProfileAsync();

            if (viewModel.ConnectionA.TryGetRememberedPassword() is { } sourcePassword
                && FindPasswordBox(SourceContentControl) is { } sourcePasswordBox)
            {
                sourcePasswordBox.Password = sourcePassword;
            }

            if (viewModel.ConnectionB.TryGetRememberedPassword() is { } targetPassword
                && FindPasswordBox(TargetContentControl) is { } targetPasswordBox)
            {
                targetPasswordBox.Password = targetPassword;
            }
        }

        /// <summary>
        /// runs once the window's native handle is available, and enables the dark title bar.
        /// </summary>
        /// <param name="e">a System.EventArgs describing the source-initialized event.</param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TryEnableDarkTitleBar();
        }

        // WPF has no managed API for the title bar; DWMWA_USE_IMMERSIVE_DARK_MODE is the documented
        // way to opt a window into the OS dark caption (Windows 10 1809+, attribute number changed at
        // 20H1 — try the current value first, then the pre-20H1 value).
        /// <summary>
        /// attempts to opt this window into the OS dark title bar via DwmSetWindowAttribute, trying the
        /// current attribute value first and falling back to the pre-20H1 value.
        /// </summary>
        private void TryEnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var useDarkMode = 1;
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            {
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkModePre20H1, ref useDarkMode, sizeof(int));
            }
        }

        /// <summary>
        /// P/Invoke declarations for the dwmapi.dll functions and constants used to enable the dark
        /// title bar.
        /// </summary>
        private static class NativeMethods
        {
            /// <summary>
            /// the current (20H1 and later) DWMWA_USE_IMMERSIVE_DARK_MODE attribute value.
            /// </summary>
            public const int DwmwaUseImmersiveDarkMode = 20;

            /// <summary>
            /// the pre-20H1 DWMWA_USE_IMMERSIVE_DARK_MODE attribute value.
            /// </summary>
            public const int DwmwaUseImmersiveDarkModePre20H1 = 19;

            /// <summary>
            /// sets a window attribute via the dwmapi.dll DwmSetWindowAttribute native function.
            /// </summary>
            /// <param name="hwnd">a System.IntPtr handle to the window to set the attribute on.</param>
            /// <param name="attribute">a System.Int32 identifying which DWM window attribute to set.</param>
            /// <param name="value">a System.Int32 passed by reference containing the attribute value to apply.</param>
            /// <param name="size">a System.Int32 containing the size, in bytes, of the value parameter.</param>
            /// <returns>returns a System.Int32 HRESULT indicating success (S_OK, zero) or failure.</returns>
            [DllImport("dwmapi.dll")]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        }

        // The chevron banner's diagonal cut is recomputed on resize so it stays proportional rather
        // than being a fixed-pixel shape that looks wrong at other window sizes. Shared by the Data
        // Sources screen's banner and the Results screens' banner (planning.md §19 addendum) — same
        // visual language on both, so the results screens don't read as a different, plainer tool.
        /// <summary>
        /// recomputes the Data Sources screen's chevron banner cut when the banner is resized.
        /// </summary>
        /// <param name="sender">a System.Object representing the banner element that raised the event.</param>
        /// <param name="e">a System.Windows.SizeChangedEventArgs describing the size change.</param>
        private void ChevronBanner_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChevronBannerCut(ChevronBanner, SourceBannerPolygon);

        /// <summary>
        /// recomputes the Results screen's chevron banner cut when the banner is resized.
        /// </summary>
        /// <param name="sender">a System.Object representing the banner element that raised the event.</param>
        /// <param name="e">a System.Windows.SizeChangedEventArgs describing the size change.</param>
        private void ResultsChevronBanner_SizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateChevronBannerCut(ResultsChevronBanner, ResultsSourceBannerPolygon);

        /// <summary>
        /// recalculates the diagonal-cut polygon points for a chevron banner, proportional to its
        /// current size.
        /// </summary>
        /// <param name="banner">a System.Windows.FrameworkElement representing the banner whose current size drives the cut proportions.</param>
        /// <param name="sourcePolygon">a System.Windows.Shapes.Polygon whose points are updated to draw the chevron cut.</param>
        private static void UpdateChevronBannerCut(FrameworkElement banner, Polygon sourcePolygon)
        {
            var width = banner.ActualWidth;
            var height = banner.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var sourceWidth = width * 0.45;
            var chevronDepth = height * 0.9;

            sourcePolygon.Points =
            [
                new Point(0, 0),
                new Point(Math.Min(width, sourceWidth + chevronDepth), 0),
                new Point(sourceWidth, height),
                new Point(0, height),
            ];
        }

        // PasswordBox intentionally isn't bound via MVVM (WPF doesn't support binding PasswordBox.Password
        // directly for security reasons), so the plaintext value is read here, at the point of use, only.
        /// <summary>
        /// handles the Test Connection button click by reading the plaintext password directly from the
        /// PasswordBox and invoking the view model's test-connection command.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var viewModel = (ConnectionSetupViewModel)button.Tag;
            var contentPresenter = FindAncestor<ContentPresenter>(button);
            if (contentPresenter?.ContentTemplate?.FindName("PasswordBox", contentPresenter) is not PasswordBox passwordBox)
            {
                return;
            }

            await viewModel.TestConnectionCommand.ExecuteAsync(passwordBox.Password);
        }

        /// <summary>
        /// handles the Refresh Databases button click by reading the plaintext password directly from the
        /// PasswordBox and invoking the view model's refresh-databases command.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private async void RefreshDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var viewModel = (ConnectionSetupViewModel)button.Tag;
            var contentPresenter = FindAncestor<ContentPresenter>(button);
            if (contentPresenter?.ContentTemplate?.FindName("PasswordBox", contentPresenter) is not PasswordBox passwordBox)
            {
                return;
            }

            await viewModel.RefreshDatabasesCommand.ExecuteAsync(passwordBox.Password);
        }

        /// <summary>
        /// handles the Compare Now button click by locating both password fields, showing the progress
        /// window, running the comparison, and then switching to the results view.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private async void CompareNowButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;
            var sourcePasswordBox = FindPasswordBox(SourceContentControl);
            var targetPasswordBox = FindPasswordBox(TargetContentControl);
            if (sourcePasswordBox is null || targetPasswordBox is null)
            {
                MessageBox.Show(this, "Couldn't locate the password fields — this is a bug, not a slow comparison. Please report it.",
                    "VK", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            void ShowResults(object? _, EventArgs _2) => ShowResultsBody();

            // A genuine separate popup, unlike the Results screen — "please wait, here's exactly
            // what's outstanding" is a different UI need than the results view, which needed to stay
            // in the same window.
            var progressWindow = new DataComparisonProgressWindow(viewModel) { Owner = this };
            progressWindow.Show();

            viewModel.ResultsReady += ShowResults;
            try
            {
                await viewModel.CompareAsync(sourcePasswordBox.Password, targetPasswordBox.Password);
            }
            finally
            {
                viewModel.ResultsReady -= ShowResults;
                progressWindow.Close();
            }
        }

        // Results replace the Data Sources screen in the SAME window rather than opening a second
        // window — a separate popup with a different look was the wrong call the first time round.
        // Schema is the default sub-view on first landing here after a compare finishes.
        /// <summary>
        /// shows the Results body, defaulting to the schema results sub-view.
        /// </summary>
        private void ShowResultsBody() => ShowSchemaResultsSubView();

        /// <summary>
        /// shows the Data Sources body and hides the Results body.
        /// </summary>
        private void ShowDataSourcesBody()
        {
            ResultsBody.Visibility = Visibility.Collapsed;
            DataSourcesBody.Visibility = Visibility.Visible;
            BottomToolbar.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// handles the Data Sources navigation button click by showing the Data Sources body.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void DataSourcesNavButton_Click(object sender, RoutedEventArgs e) => ShowDataSourcesBody();

        // "Tables & views" and "Data comparison" are real navigation destinations, not sub-view togglers
        // scoped to an already-visible Results screen — each ensures the Results screen itself is showing
        // before switching sub-view, regardless of which screen was showing beforehand. Previously,
        // clicking "Data Sources" after a compare finished was a dead end: nothing could bring the
        // Results screen back (its data was still in memory the whole time, just unreachable through the
        // UI), costing a real comparison run to redo. Navigation must never trap the user like that again.
        /// <summary>
        /// ensures the Results body is visible and the Data Sources body and bottom toolbar are hidden,
        /// regardless of which screen was showing beforehand.
        /// </summary>
        private void EnsureResultsBodyVisible()
        {
            DataSourcesBody.Visibility = Visibility.Collapsed;
            ResultsBody.Visibility = Visibility.Visible;
            BottomToolbar.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// ensures the Results body is visible and switches it to the schema results sub-view.
        /// </summary>
        private void ShowSchemaResultsSubView()
        {
            EnsureResultsBodyVisible();
            DataResultsSubView.Visibility = Visibility.Collapsed;
            SchemaResultsSubView.Visibility = Visibility.Visible;
            ExportHtmlButton.Content = "Export Schema Report to HTML...";
        }

        /// <summary>
        /// ensures the Results body is visible and switches it to the data comparison results sub-view.
        /// </summary>
        private void ShowDataResultsSubView()
        {
            EnsureResultsBodyVisible();
            SchemaResultsSubView.Visibility = Visibility.Collapsed;
            DataResultsSubView.Visibility = Visibility.Visible;
            ExportHtmlButton.Content = "Export Data Comparison Report to HTML...";
        }

        /// <summary>
        /// handles the Schema results navigation button click by switching to the schema results
        /// sub-view.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void SchemaResultsNavButton_Click(object sender, RoutedEventArgs e) => ShowSchemaResultsSubView();

        /// <summary>
        /// handles the Data results navigation button click by switching to the data comparison results
        /// sub-view.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void DataResultsNavButton_Click(object sender, RoutedEventArgs e) => ShowDataResultsSubView();

        // The export button is shared between both Results sub-views (planning.md §19 addendum) —
        // previously it always exported the schema report regardless of which tab was showing, which
        // was the same schema-vs-data confusion the Data comparison tab itself was built to resolve, just
        // showing up again in the export feature. It now exports whichever sub-view is actually visible.
        /// <summary>
        /// handles the Export HTML button click by generating and saving whichever results sub-view
        /// report (schema or data comparison) is currently visible.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void ExportHtmlButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;
            var isDataTabActive = DataResultsSubView.Visibility == Visibility.Visible;

            var html = isDataTabActive ? viewModel.GenerateDataComparisonHtmlReport() : viewModel.GenerateSchemaHtmlReport();
            if (html is null)
            {
                var comparisonKind = isDataTabActive ? "data" : "schema";
                MessageBox.Show(this, $"Run a {comparisonKind} comparison first.", "VK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var reportKind = isDataTabActive ? "Data" : "Schema";
            var dialog = new SaveFileDialog
            {
                FileName = $"VK-{reportKind}-Compare-{DateTime.Now:yyyyMMdd-HHmmss}.html",
                Filter = "HTML file (*.html)|*.html",
                DefaultExt = ".html",
            };

            if (dialog.ShowDialog(this) == true)
            {
                File.WriteAllText(dialog.FileName, html);
            }
        }

        // GridView columns don't support "*" star sizing, so left alone the table would leave dead
        // space on the right (or clip) as the window resizes. Scale every column proportionally to its
        // designed width instead of just stretching the last one, clamped to a minimum so text stays
        // legible — the closest approximation of "*" sizing GridView allows. Shared between the Schema
        // grid and the Data comparison grid (planning.md §19 addendum).
        /// <summary>
        /// the designed base column widths for the Schema results GridView.
        /// </summary>
        private static readonly double[] SchemaColumnBaseWidths = [50, 120, 60, 220, 220, 60, 120];

        /// <summary>
        /// the minimum column widths for the Schema results GridView, below which text stops being
        /// legible.
        /// </summary>
        private static readonly double[] SchemaColumnMinWidths = [40, 90, 50, 120, 120, 50, 90];

        /// <summary>
        /// the designed base column widths for the Data comparison results GridView.
        /// </summary>
        private static readonly double[] DataComparisonColumnBaseWidths = [220, 100, 100, 100, 90, 130, 130, 120];

        /// <summary>
        /// the minimum column widths for the Data comparison results GridView, below which text stops
        /// being legible.
        /// </summary>
        private static readonly double[] DataComparisonColumnMinWidths = [140, 70, 70, 70, 60, 90, 90, 80];

        /// <summary>
        /// handles the Schema results ListView size change by proportionally resizing its GridView
        /// columns.
        /// </summary>
        /// <param name="sender">a System.Object representing the ListView that raised the event.</param>
        /// <param name="e">a System.Windows.SizeChangedEventArgs describing the size change.</param>
        private void SchemaListView_SizeChanged(object sender, SizeChangedEventArgs e) =>
            ResizeGridViewColumnsProportionally((ListView)sender, SchemaColumnBaseWidths, SchemaColumnMinWidths);

        /// <summary>
        /// handles the Data comparison results ListView size change by proportionally resizing its
        /// GridView columns.
        /// </summary>
        /// <param name="sender">a System.Object representing the ListView that raised the event.</param>
        /// <param name="e">a System.Windows.SizeChangedEventArgs describing the size change.</param>
        private void DataComparisonListView_SizeChanged(object sender, SizeChangedEventArgs e) =>
            ResizeGridViewColumnsProportionally((ListView)sender, DataComparisonColumnBaseWidths, DataComparisonColumnMinWidths);

        /// <summary>
        /// proportionally scales a ListView's GridView columns to fill the available width, clamped to
        /// each column's minimum width.
        /// </summary>
        /// <param name="listView">a System.Windows.Controls.ListView whose GridView columns are to be resized.</param>
        /// <param name="baseWidths">a System.Double array containing the designed base width for each column.</param>
        /// <param name="minWidths">a System.Double array containing the minimum allowed width for each column.</param>
        private static void ResizeGridViewColumnsProportionally(ListView listView, double[] baseWidths, double[] minWidths)
        {
            if (listView.View is not GridView { Columns.Count: > 0 } gridView || gridView.Columns.Count != baseWidths.Length)
            {
                return;
            }

            const double scrollbarAllowance = 25;
            var availableWidth = Math.Max(0, listView.ActualWidth - scrollbarAllowance);
            var totalBaseWidth = baseWidths.Sum();
            var scale = availableWidth / totalBaseWidth;

            for (var i = 0; i < gridView.Columns.Count; i++)
            {
                gridView.Columns[i].Width = Math.Max(minWidths[i], baseWidths[i] * scale);
            }
        }

        /// <summary>
        /// handles the Back To Data Sources button click by showing the Data Sources body.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void BackToDataSourcesButton_Click(object sender, RoutedEventArgs e) => ShowDataSourcesBody();

        /// <summary>
        /// handles the Schema results list box selection change by updating the view model's selected
        /// schema row.
        /// </summary>
        /// <param name="sender">a System.Object representing the list box that raised the event.</param>
        /// <param name="e">a System.Windows.Controls.SelectionChangedEventArgs describing the selection change.</param>
        private void SchemaListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SelectedSchemaRow = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as SchemaObjectRow;
            }
        }

        /// <summary>
        /// handles the Data comparison results list view selection change by updating the view model's
        /// selected data comparison row.
        /// </summary>
        /// <param name="sender">a System.Object representing the list view that raised the event.</param>
        /// <param name="e">a System.Windows.Controls.SelectionChangedEventArgs describing the selection change.</param>
        private void DataComparisonListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SelectedDataComparisonRow = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as DataComparisonRow;
            }
        }

        /// <summary>
        /// handles the Cancel button click: cancels an in-progress comparison if one is running,
        /// otherwise clears both connection forms' fields and password boxes.
        /// </summary>
        /// <param name="sender">a System.Object representing the button that raised the event.</param>
        /// <param name="e">a System.Windows.RoutedEventArgs describing the click event.</param>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

            // While a comparison is running, Cancel means "stop the comparison" — clearing the fields
            // underneath it would be a confusing, unrelated action.
            if (viewModel.IsComparing)
            {
                viewModel.CancelCompare();
                return;
            }

            viewModel.ConnectionA.ClearFields();
            viewModel.ConnectionB.ClearFields();

            if (FindPasswordBox(SourceContentControl) is { } sourcePasswordBox)
            {
                sourcePasswordBox.Password = string.Empty;
            }

            if (FindPasswordBox(TargetContentControl) is { } targetPasswordBox)
            {
                targetPasswordBox.Password = string.Empty;
            }
        }

        /// <summary>
        /// finds the PasswordBox nested inside a ContentControl's applied template.
        /// </summary>
        /// <param name="contentControl">a System.Windows.Controls.ContentControl whose visual tree is searched for a PasswordBox.</param>
        /// <returns>returns a System.Windows.Controls.PasswordBox found within the content control's template, or null if none was found.</returns>
        private static PasswordBox? FindPasswordBox(ContentControl contentControl)
        {
            var contentPresenter = FindDescendant<ContentPresenter>(contentControl);
            return contentPresenter?.ContentTemplate?.FindName("PasswordBox", contentPresenter) as PasswordBox;
        }

        /// <summary>
        /// walks up the visual tree from the given element to find the nearest ancestor of type T.
        /// </summary>
        /// <param name="element">a System.Windows.DependencyObject to start the ancestor search from.</param>
        /// <returns>returns a T representing the nearest matching ancestor, or null if none was found.</returns>
        private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(element);
            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        /// <summary>
        /// recursively searches the visual tree beneath the given element to find the first descendant
        /// of type T.
        /// </summary>
        /// <param name="parent">a System.Windows.DependencyObject to start the descendant search from.</param>
        /// <returns>returns a T representing the first matching descendant found, or null if none was found.</returns>
        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                if (FindDescendant<T>(child) is { } descendant)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
