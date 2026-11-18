using Microsoft.UI.Xaml;

namespace Krono_Agent;
public partial class App : Application
{
    Window? window;
    public App()
    {
        var english = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = english;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = english;
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
        SetThreadPreferredUILanguages(8, "en-US\0", out _);
        InitializeComponent();
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool SetThreadPreferredUILanguages(uint flags, string languages, out uint count);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
