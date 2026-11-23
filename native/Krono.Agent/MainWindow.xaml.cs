using Microsoft.UI.Xaml;

namespace Krono_Agent;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    readonly TrayIcon tray;
    bool confirmingClose;
    bool closeApproved;
    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
        if (Content is FrameworkElement root) {
            root.ActualThemeChanged += (_, _) => { KronoDesign.ApplyControlAccents(root); UpdateCaptionColors(); };
            KronoDesign.ApplyControlAccents(root);
            UpdateCaptionColors();
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), iconPath, RestoreFromTray, ExitFromTray);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Maximize();

        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Closing += (_, args) => {
            if (closeApproved || !tray.IsAvailable || Environment.GetEnvironmentVariable("KRONO_SMOKE_CLOSE_WINDOW") == "1") return;
            args.Cancel = true;
            if (RootFrame.Content is MainPage page) page.PauseGames("Krono was hidden to the tray");
            AppWindow.Hide();
        };
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated && RootFrame.Content is MainPage page) page.PauseGames("Krono lost window focus"); };
        Closed += (_, _) => {
            tray.Dispose();
            if (RootFrame.Content is MainPage page) page.PauseGames("Krono window closed");
        };
    }
    internal void RestoreFromTray()
    {
        if (!AppWindow.IsVisible) AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    async void ExitFromTray()
    {
        if (closeApproved || confirmingClose) return;
        if (RootFrame.Content is not MainPage { HasBackgroundWork: true } page) {
            CloseApproved();
            return;
        }
        RestoreFromTray();
        confirmingClose = true;
        try {
            if (await page.Confirm("Quit Krono Agent?", "A task or update download is still running. Quitting will interrupt it.", "Quit"))
                CloseApproved();
        } catch (Exception error) { page.Error(error); }
        finally { confirmingClose = false; }
    }

    internal void CloseApproved()
    {
        closeApproved = true;
        Close();
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
        var theme = light ? ElementTheme.Light : ElementTheme.Dark;
        Windows.UI.Color Tint(string role) => ((Microsoft.UI.Xaml.Media.SolidColorBrush)KronoDesign.ThemeBrush(role, theme)).Color;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Tint("ButtonBackgroundPointerOver");
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Tint("ButtonBackgroundPressed");
    }
}
