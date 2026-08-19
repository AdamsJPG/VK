using System.Runtime.InteropServices;
using DataCompare.App.Cli;

namespace DataCompare.App
{

    /// <summary>
    /// The process entry point. Command-line arguments switch into headless CLI mode (see <see
    /// cref="CliRunner"/>) instead of starting the WPF application — the WPF SDK's own auto-generated
    /// entry point (from App.xaml's ApplicationDefinition) always launches straight into the GUI with
    /// no chance to inspect argv first, so it's replaced by this class via the csproj's StartupObject.
    /// </summary>
    public static class Program
    {
        // A WinExe-subsystem process has no console attached by default, so plain Console.Write calls
        // in CLI mode would silently go nowhere when launched from an existing terminal. Attaching to
        // the parent process's console (the terminal that launched VK.exe) before writing anything
        // makes CLI output visible there — the standard workaround for a hybrid GUI/console .NET app.
        // Has no effect (and is harmless) when there's no parent console, e.g. a scheduled task.
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private const int AttachParentProcess = -1;

        /// <summary>
        /// the process entry point: launches the WPF application when given no arguments, otherwise
        /// runs headless CLI mode and returns its exit code.
        /// </summary>
        /// <param name="args">a System.String array holding the command-line arguments the process was launched with</param>
        /// <returns>returns a System.Int32 process exit code — 0 for success, non-zero for a CLI failure; the WPF path always returns 0</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
                return 0;
            }

            AttachConsole(AttachParentProcess);
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }
    }
}
