using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DataCompare.App.ViewModels;
using Microsoft.Win32;

namespace DataCompare.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = VkIconFactory.Render(64);
        Loaded += MainWindow_Loaded;
    }

    // Restores the last-used server/database/user once the visual tree is realized — the
    // PasswordBoxes aren't reachable via FindPasswordBox until the ContentControls have applied
    // their templates, which isn't guaranteed yet in the constructor.
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableDarkTitleBar();
    }

    // WPF has no managed API for the title bar; DWMWA_USE_IMMERSIVE_DARK_MODE is the documented
    // way to opt a window into the OS dark caption (Windows 10 1809+, attribute number changed at
    // 20H1 — try the current value first, then the pre-20H1 value).
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

    private static class NativeMethods
    {
        public const int DwmwaUseImmersiveDarkMode = 20;
        public const int DwmwaUseImmersiveDarkModePre20H1 = 19;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    // The chevron banner's diagonal cut is recomputed on resize so it stays proportional rather
    // than being a fixed-pixel shape that looks wrong at other window sizes. Shared by the Data
    // Sources screen's banner and the Results screens' banner (planning.md §19 addendum) — same
    // visual language on both, so the results screens don't read as a different, plainer tool.
    private void ChevronBanner_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChevronBannerCut(ChevronBanner, SourceBannerPolygon);

    private void ResultsChevronBanner_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateChevronBannerCut(ResultsChevronBanner, ResultsSourceBannerPolygon);

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
    private void ShowResultsBody() => ShowSchemaResultsSubView();

    private void ShowDataSourcesBody()
    {
        ResultsBody.Visibility = Visibility.Collapsed;
        DataSourcesBody.Visibility = Visibility.Visible;
        BottomToolbar.Visibility = Visibility.Visible;
    }

    private void DataSourcesNavButton_Click(object sender, RoutedEventArgs e) => ShowDataSourcesBody();

    // "Tables & views" and "Data comparison" are real navigation destinations, not sub-view togglers
    // scoped to an already-visible Results screen — each ensures the Results screen itself is showing
    // before switching sub-view, regardless of which screen was showing beforehand. Previously,
    // clicking "Data Sources" after a compare finished was a dead end: nothing could bring the
    // Results screen back (its data was still in memory the whole time, just unreachable through the
    // UI), costing a real comparison run to redo. Navigation must never trap the user like that again.
    private void EnsureResultsBodyVisible()
    {
        DataSourcesBody.Visibility = Visibility.Collapsed;
        ResultsBody.Visibility = Visibility.Visible;
        BottomToolbar.Visibility = Visibility.Collapsed;
    }

    private void ShowSchemaResultsSubView()
    {
        EnsureResultsBodyVisible();
        DataResultsSubView.Visibility = Visibility.Collapsed;
        SchemaResultsSubView.Visibility = Visibility.Visible;
    }

    private void ShowDataResultsSubView()
    {
        EnsureResultsBodyVisible();
        SchemaResultsSubView.Visibility = Visibility.Collapsed;
        DataResultsSubView.Visibility = Visibility.Visible;
    }

    private void SchemaResultsNavButton_Click(object sender, RoutedEventArgs e) => ShowSchemaResultsSubView();

    private void DataResultsNavButton_Click(object sender, RoutedEventArgs e) => ShowDataResultsSubView();

    // The export button is shared between both Results sub-views (planning.md §19 addendum) —
    // previously it always exported the schema report regardless of which tab was showing, which
    // was the same schema-vs-data confusion the Data comparison tab itself was built to resolve, just
    // showing up again in the export feature. It now exports whichever sub-view is actually visible.
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
    private static readonly double[] SchemaColumnBaseWidths = [50, 120, 60, 220, 220, 60, 120];
    private static readonly double[] SchemaColumnMinWidths = [40, 90, 50, 120, 120, 50, 90];
    private static readonly double[] DataComparisonColumnBaseWidths = [220, 100, 100, 100, 90, 130, 130];
    private static readonly double[] DataComparisonColumnMinWidths = [140, 70, 70, 70, 60, 90, 90];

    private void SchemaListView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeGridViewColumnsProportionally((ListView)sender, SchemaColumnBaseWidths, SchemaColumnMinWidths);

    private void DataComparisonListView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeGridViewColumnsProportionally((ListView)sender, DataComparisonColumnBaseWidths, DataComparisonColumnMinWidths);

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

    private void BackToDataSourcesButton_Click(object sender, RoutedEventArgs e) => ShowDataSourcesBody();

    private void SchemaListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedSchemaRow = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as SchemaObjectRow;
        }
    }

    private void DataComparisonListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedDataComparisonRow = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as DataComparisonRow;
        }
    }

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

    private static PasswordBox? FindPasswordBox(ContentControl contentControl)
    {
        var contentPresenter = FindDescendant<ContentPresenter>(contentControl);
        return contentPresenter?.ContentTemplate?.FindName("PasswordBox", contentPresenter) as PasswordBox;
    }

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
