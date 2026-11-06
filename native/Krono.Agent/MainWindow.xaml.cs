using Microsoft.UI.Xaml;

namespace Krono_Agent;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
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

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Maximize();

        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Closing += ConfirmClose;
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated && RootFrame.Content is MainPage page) page.PauseGames("Krono lost window focus"); };
        Closed += (_, _) => { if (RootFrame.Content is MainPage page) page.PauseGames("Krono window closed"); };
    }
    async void ConfirmClose(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (closeApproved || RootFrame.Content is not MainPage { HasBackgroundWork: true } page) return;
        args.Cancel = true;
        if (confirmingClose) return;
        confirmingClose = true;
        try {
            if (await page.Confirm("Close Krono Agent?", "A task or update download is still running. Closing will interrupt it.", "Close app"))
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
    }
}
