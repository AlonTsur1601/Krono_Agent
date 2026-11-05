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
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Maximize();

        RootFrame.Navigate(typeof(MainPage));
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated && RootFrame.Content is MainPage page) page.PauseGames("Krono lost window focus"); };
        Closed += (_, _) => { if (RootFrame.Content is MainPage page) page.PauseGames("Krono window closed"); };
    }
    public void ShowHalloween(bool enabled)
    {
        HalloweenTrim.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateCaptionColors();
    }

    void UpdateCaptionColors()
    {
        var light = Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light;
        var foreground = HalloweenTrim.Visibility == Visibility.Visible ? Windows.UI.Color.FromArgb(255, 235, 210, 253)
            : light ? Windows.UI.Color.FromArgb(255, 16, 35, 31) : Windows.UI.Color.FromArgb(255, 241, 245, 249);
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }
}
