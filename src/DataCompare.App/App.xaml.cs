using System.Windows;
using System.Windows.Threading;

namespace DataCompare.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan SplashDuration = TimeSpan.FromMilliseconds(1400);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dev utility: regenerate Assets/vk.ico from VkIconFactory after a design tweak.
        // Run with `dotnet run --project src/DataCompare.App -- --generate-icon` from the project dir.
        if (e.Args.Contains("--generate-icon"))
        {
            IconGenerator.GenerateIcoFile("Assets/vk.ico");
            Shutdown();
            return;
        }

        Resources["DatabaseCylinderGeometry"] = VkGeometry.CreateCylinderSilhouette();
        Resources["DatabaseCylinderSeams"] = VkGeometry.CreateSeamLines();
        Resources["AccentBrush"] = AppTheme.AccentBrush;
        Resources["AccentColor"] = AppTheme.AccentColor;

        var splash = new SplashWindow();
        splash.Show();

        var timer = new DispatcherTimer { Interval = SplashDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            splash.Close();
        };
        timer.Start();
    }
}
