using Microsoft.UI.Xaml;

namespace Krono_Agent;
public partial class App : Application
{
    static readonly object faultLock = new();
    Window? window;
    public App()
    {
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = english;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = english;
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
        SetThreadPreferredUILanguages(8, "en-US\0", out _);
        UnhandledException += (_, args) => RecordFault("WinUI", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            if (args.ExceptionObject is Exception error) RecordFault("Managed runtime", error);
        };
        InitializeComponent();
    }

    internal static void RecordFault(string component, Exception error)
    {
        // Keep stack frames and error types only. Exception messages can contain user inputs or credentials.
        try {
            lock (faultLock) {
                Directory.CreateDirectory(Workspace.Home);
                var file = Path.Combine(Workspace.Home, "native-errors.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024)
                    File.Move(file, file + ".previous", true);
                var trace = new System.Diagnostics.StackTrace(error, false).ToString();
                File.AppendAllText(file, $"[{DateTimeOffset.Now:O}] {component}: {error.GetType().Name} (0x{error.HResult:X8})\n{trace}\n");
            }
        } catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException) { }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool SetThreadPreferredUILanguages(uint flags, string languages, out uint count);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
