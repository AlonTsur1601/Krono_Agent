using Microsoft.UI.Xaml;

namespace Krono_Agent;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (Content is FrameworkElement root) {
            root.ActualThemeChanged += (_, _) => UpdateCaptionColors();
            UpdateCaptionColors();
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 900));

        RootFrame.Navigate(typeof(MainPage));
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated && RootFrame.Content is MainPage page) page.PauseGames("Krono lost window focus"); };
        Closed += (_, _) => { if (RootFrame.Content is MainPage page) page.PauseGames("Krono window closed"); };
    }
    void UpdateCaptionColors()
    {
        var light = Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light;
        var foreground = light ? Windows.UI.Color.FromArgb(255, 16, 35, 31) : Windows.UI.Color.FromArgb(255, 241, 245, 249);
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }
}
