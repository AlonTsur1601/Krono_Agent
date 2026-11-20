using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace Krono_Agent;

public sealed class BrowserWorkspace : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly MainPage owner;
    readonly TabView tabs = new() { VerticalAlignment = VerticalAlignment.Stretch };
    readonly KronoTextBox address = new() { PlaceholderText = "Address or search", MinWidth = 120, PressMotionEnabled = false };
    readonly Button go;
    readonly Dictionary<TabViewItem, WebView2> views = new();
    readonly Dictionary<WebView2, Grid> pages = new();
    readonly Grid browserContent = new();
    readonly Grid pageHost = new();
    readonly TextBlock linkDestination = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 500 };
    readonly Border linkStatus = new() {
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(8, 0, 8, 8), Padding = new Thickness(9, 4, 9, 4),
        CornerRadius = new CornerRadius(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    Grid? activePage;
    Task<CoreWebView2Environment>? browserEnvironment;
    readonly HashSet<TabViewItem> privateTabs = new();
    readonly HashSet<WebView2> readyViews = new();
    WebView2? Current => tabs.SelectedItem is TabViewItem tab ? views.GetValueOrDefault(tab) : null;
    const string DefaultHome = "https://www.google.com/?hl=en";
    const string LinkMessage = "krono:link-preview:";
    const string LinkPreviewScript = """
        (() => {
            const send = href => window.chrome?.webview?.postMessage('krono:link-preview:' + href);
            let hovered = null;
            addEventListener('pointerover', event => {
                const link = event.composedPath().find(node => node instanceof Element && node.matches('a[href], area[href]'));
                if (link === hovered) return;
                hovered = link;
                const href = link?.getAttribute('href');
                try { send(href ? new URL(href, document.baseURI).href : ''); }
                catch { send(''); }
            }, true);
            addEventListener('pointerout', event => {
                if (!hovered || hovered.contains(event.relatedTarget)) return;
                hovered = null;
                send('');
            }, true);
        })();
        """;
    string HomePage => Uri.TryCreate(workspace.Setting("browser.homePage"), UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && uri.AbsoluteUri != "https://www.google.com/"
        ? uri.AbsoluteUri : DefaultHome;
    public BrowserWorkspace(Workspace workspace, MainPage owner)
    {
        this.workspace = workspace; this.owner = owner;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(address, "BrowserAddress");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(linkDestination, "BrowserLinkDestination");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(ToolbarButton("Back", Symbol.Back, () => { if (Current?.CanGoBack == true) Current.GoBack(); }));
        bar.Children.Add(ToolbarButton("Forward", Symbol.Forward, () => { if (Current?.CanGoForward == true) Current.GoForward(); }));
        bar.Children.Add(ToolbarButton("Reload", Symbol.Refresh, () => Current?.Reload()));
        address.PreviewKeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Go(); } };
        go = ToolbarButton("Go", Symbol.Go, Go);
        bar.Children.Add(go);
        var menu = new MenuFlyout();
        void AddMenuAction(string label, Symbol symbol, Action action)
        {
            var item = new MenuFlyoutItem { Text = label, Icon = ToolbarIcon(symbol) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        var more = ToolbarButton("More options", Symbol.More);
        more.Flyout = menu;
        bar.Children.Add(more);
        AddMenuAction("Private tab", Symbol.Permissions, () => _ = Open(HomePage, isPrivate: true, isHome: true));
        AddMenuAction("Find on page", Symbol.Find, async () => {
            var input = new KronoTextBox { PlaceholderText = "Find text", PressMotionEnabled = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "BrowserFindInput");
            var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = "Find on page", Content = input, PrimaryButtonText = "Find next", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Primary };
            var submittedFromField = false;
            dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
            input.PreviewKeyDown += (_, args) => {
                if (args.Key != Windows.System.VirtualKey.Enter) return;
                args.Handled = true;
                submittedFromField = true;
                dialog.Hide();
            };
            var result = await dialog.ShowAsync();
            if ((submittedFromField || result == ContentDialogResult.Primary) && Current is { } view)
                await view.ExecuteScriptAsync("window.find(" + System.Text.Json.JsonSerializer.Serialize(input.Text) + ",false,false,true)");
        });
        AddMenuAction("Bookmark", Symbol.Favorite, () => { if (tabs.SelectedItem is TabViewItem tab && !privateTabs.Contains(tab)) workspace.SetSetting("browser.bookmark." + (Current?.Source?.ToString() ?? ""), Current?.Source?.ToString() ?? ""); });
        AddMenuAction("History & bookmarks", Symbol.Library, async () => {
            var list = new ListView { ItemsSource = workspace.Query("SELECT value FROM settings WHERE key LIKE 'browser.history.%' OR key LIKE 'browser.bookmark.%' ORDER BY key DESC LIMIT 100").Select(r => r["value"]!.ToString()).ToArray(), MaxHeight = 400 };
            var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = "History & bookmarks", Content = list, PrimaryButtonText = "Open selected", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is string url) await Open(url);
        });
        AddMenuAction("Open externally", Symbol.OpenWith, () => { if (Current?.Source is Uri uri && uri.Scheme is "https" or "http") Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); });
        AddMenuAction("Google account", Symbol.Contact, async () => {
            try {
                if (workspace.GoogleEmail is { } email) {
                    var dialog = new KronoDialog {
                        XamlRoot = XamlRoot, Title = "Google account",
                        Content = new TextBlock { Text = email, TextWrapping = TextWrapping.Wrap },
                        PrimaryButtonText = "Open Google Account", CloseButtonText = "Close"
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        Process.Start(new ProcessStartInfo("https://myaccount.google.com/?authuser=" + Uri.EscapeDataString(email)) { UseShellExecute = true })?.Dispose();
                } else if (await owner.Confirm("Link Google to Krono?", "Choose the Google account you want to link to this Krono account.")) {
                    await owner.AuthorizeGoogle(true);
                }
            }
            catch (Exception error) { owner.Error(error); }
        });
        AddMenuAction("Zoom in", Symbol.ZoomIn, () => _ = Current?.ExecuteScriptAsync("document.body.style.zoom=(parseFloat(document.body.style.zoom)||1)+0.1"));
        AddMenuAction("Zoom out", Symbol.ZoomOut, () => _ = Current?.ExecuteScriptAsync("document.body.style.zoom=Math.max(.3,(parseFloat(document.body.style.zoom)||1)-0.1)"));
        browserContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        browserContent.RowDefinitions.Add(new RowDefinition());
        address.Margin = new Thickness(6, 0, 6, 4);
        browserContent.Children.Add(address);
        browserContent.Children.Add(pageHost);
        Grid.SetRow(pageHost, 1);
        linkStatus.Child = linkDestination;
        browserContent.Children.Add(linkStatus);
        Grid.SetRow(linkStatus, 1);
        void PaintBrowser()
        {
            browserContent.Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
            linkStatus.Background = KronoDesign.ThemeBrush("KronoSidebar", ActualTheme);
        }
        ActualThemeChanged += (_, _) => PaintBrowser();
        KronoInputMotion.RegisterPaletteRefresh(this, PaintBrowser);
        PaintBrowser();
        var frame = new Grid();
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition());
        frame.Children.Add(KronoDesign.CreateTabStrip(tabs, () => _ = Open(HomePage, isHome: true), bar));
        frame.Children.Add(browserContent);
        Grid.SetRow(browserContent, 1);
        Children.Add(KronoDesign.FrameTabContent(frame, "KronoSidebar"));
        tabs.TabCloseRequested += (sender, args) => {
            if (args.Tab is not TabViewItem tab || !views.Remove(tab, out var view)) return;
            if (pages.Remove(view, out var page)) pageHost.Children.Remove(page);
            readyViews.Remove(view); view.Close(); privateTabs.Remove(tab); tabs.TabItems.Remove(tab);
            if (tabs.TabItems.Count == 0) _ = Open(HomePage, isHome: true);
        };
        tabs.SelectionChanged += (_, _) => {
            if (activePage is not null) activePage.Visibility = Visibility.Collapsed;
            activePage = Current is { } view ? pages.GetValueOrDefault(view) : null;
            if (activePage is not null) activePage.Visibility = Visibility.Visible;
            linkStatus.Visibility = Visibility.Collapsed;
            UpdateAddress();
        };
        Loaded += async (_, _) => { if (tabs.TabItems.Count == 0) await Open(HomePage, isHome: true); };
    }
    static FontIcon ToolbarIcon(Symbol symbol) => new() {
        Glyph = char.ConvertFromUtf32((int)symbol),
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
        FontSize = 14
    };

    static Button ToolbarButton(string label, Symbol symbol, Action? action = null)
    {
        var button = new Button { Content = ToolbarIcon(symbol), Width = 26, Height = 26 };
        KronoInputMotion.StyleButton(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }
    void UpdateAddress()
    {
        address.Text = Current?.Source is { Scheme: "http" or "https" } source ? source.ToString() : "";
        address.IsEnabled = go.IsEnabled = Current is { } view && readyViews.Contains(view);
    }
    void Go()
    {
        var input = address.Text.Trim();
        if (input.Length == 0 || Current is not { } view || !readyViews.Contains(view)) return;
        try {
            if (!input.Contains(' ') && input.Contains('.') && !input.Contains("://")) input = "https://" + input;
            var url = Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : new Uri("https://www.google.com/search?hl=en&q=" + Uri.EscapeDataString(input));
            view.Source = url;
        } catch (Exception error) { owner.Error(error); }
    }
    async Task Open(string url, bool isPrivate = false, bool isHome = false)
    {
        try {
            var view = new WebView2 { Visibility = Visibility.Collapsed };
            var tabName = isPrivate ? "Private tab" : "New tab";
            var start = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
            start.Children.Add(isPrivate ? new SymbolIcon(Symbol.Permissions) : KronoDesign.NavigationIcon(Symbol.World));
            start.Children.Add(new TextBlock { Text = tabName, FontSize = 28, TextAlignment = TextAlignment.Center });
            var builtInHome = isHome && url == DefaultHome;
            var search = new KronoTextBox { PlaceholderText = "Search Google or enter an address", Width = 340, MaxWidth = 340,
                PressMotionEnabled = false, Visibility = builtInHome ? Visibility.Visible : Visibility.Collapsed };
            search.PreviewKeyDown += (_, args) => {
                if (args.Key != Windows.System.VirtualKey.Enter || !readyViews.Contains(view) || Current != view ||
                    string.IsNullOrWhiteSpace(search.Text)) return;
                args.Handled = true;
                address.Text = search.Text;
                Go();
            };
            start.Children.Add(search);
            var startStatus = new TextBlock { Text = "Opening your home page…", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Opacity = .72, MaxWidth = 320 };
            start.Children.Add(startStatus);
            if (builtInHome) startStatus.Visibility = Visibility.Collapsed;
            var retry = new Button { Content = "Retry home page", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
            retry.Click += (_, _) => view.Source = new Uri(HomePage);
            start.Children.Add(retry);
            var content = new Grid { Visibility = Visibility.Collapsed };
            void Paint() => content.Background = KronoDesign.ThemeBrush("KronoSurface", content.ActualTheme);
            Paint();
            content.ActualThemeChanged += (_, _) => Paint();
            KronoInputMotion.RegisterPaletteRefresh(content, Paint);
            content.Children.Add(view); content.Children.Add(start);
            var tab = new TabViewItem { Header = tabName };
            KronoDesign.StyleTabHeader(tab);
            pages.Add(view, content);
            pageHost.Children.Add(content);
            views[tab] = view; if (isPrivate) privateTabs.Add(tab); tabs.TabItems.Add(tab); tabs.SelectedItem = tab;
            var folder = Path.Combine(Workspace.Home, "native-browser", workspace.AccountId, "default");
            if (browserEnvironment is null or { IsFaulted: true } or { IsCanceled: true })
                browserEnvironment = CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions { Language = "en-US" }).AsTask();
            var environment = await browserEnvironment;
            if (!views.ContainsKey(tab)) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = isPrivate;
            options.ProfileName = isPrivate ? "Private" : "Default";
            await view.EnsureCoreWebView2Async(environment, options);
            if (!views.ContainsKey(tab)) return;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.Settings.IsWebMessageEnabled = true;
            void UpdateLinkPreview(CoreWebView2WebMessageReceivedEventArgs args)
            {
                if (Current != view) return;
                string message;
                try { message = args.TryGetWebMessageAsString(); }
                catch (ArgumentException) { return; }
                if (message is null || !message.StartsWith(LinkMessage, StringComparison.Ordinal) || message.Length > 8192) return;
                var destination = message[LinkMessage.Length..];
                if (destination.Length > 0 && (!Uri.TryCreate(destination, UriKind.Absolute, out var target) ||
                    target.Scheme is not ("http" or "https" or "mailto" or "tel" or "file"))) return;
                linkDestination.Text = destination;
                linkStatus.MaxWidth = Math.Max(0, pageHost.ActualWidth - 16);
                linkStatus.Visibility = linkDestination.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            void TrackFrame(CoreWebView2Frame frame)
            {
                frame.WebMessageReceived += (_, args) => UpdateLinkPreview(args);
                frame.FrameCreated += (_, args) => TrackFrame(args.Frame);
            }
            view.CoreWebView2.WebMessageReceived += (_, args) => UpdateLinkPreview(args);
            view.CoreWebView2.FrameCreated += (_, args) => TrackFrame(args.Frame);
            await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(LinkPreviewScript);
            if (!views.ContainsKey(tab)) return;
            string? homeAddress = null;
            void UpdateTabTitle()
            {
                var title = view.CoreWebView2.DocumentTitle;
                tab.Header = isHome || string.IsNullOrWhiteSpace(title) ? tabName : (isPrivate ? "Private · " : "") + title;
            }
            view.CoreWebView2.NavigationStarting += (_, args) => {
                if (Current == view) linkStatus.Visibility = Visibility.Collapsed;
                if (isHome && (builtInHome || homeAddress is not null && args.Uri != homeAddress)) isHome = false;
                search.Visibility = Visibility.Collapsed;
                startStatus.Visibility = Visibility.Visible;
                if (view.Visibility != Visibility.Visible) {
                    startStatus.Text = "Opening page…";
                    retry.Visibility = Visibility.Collapsed;
                }
            };
            view.CoreWebView2.DocumentTitleChanged += (_, _) => UpdateTabTitle();
            view.CoreWebView2.NavigationCompleted += (_, args) => {
                if (isHome) homeAddress = view.Source?.AbsoluteUri;
                UpdateTabTitle();
                if (Current == view && view.Source?.ToString() != "about:blank") address.Text = view.Source?.ToString() ?? "";
                if (!args.IsSuccess && args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                if (args.IsSuccess) {
                    start.Visibility = Visibility.Collapsed;
                    view.Visibility = Visibility.Visible;
                } else {
                    view.Visibility = Visibility.Collapsed;
                    start.Visibility = Visibility.Visible;
                    startStatus.Text = "This page is unavailable. Try your home page again or enter another address.";
                    retry.Visibility = Visibility.Visible;
                }
                if (args.IsSuccess && !isPrivate && view.Source?.Scheme is "http" or "https") workspace.SetSetting("browser.history." + Workspace.Now, view.Source.ToString());
            };
            view.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https") _ = Open(args.Uri, isPrivate); };
            view.CoreWebView2.PermissionRequested += async (_, args) => { using var deferral = args.GetDeferral(); args.State = CoreWebView2PermissionState.Deny; try { if (await owner.Confirm("Website permission", args.Uri + " requests " + args.PermissionKind)) args.State = CoreWebView2PermissionState.Allow; } catch { args.State = CoreWebView2PermissionState.Deny; } };
            view.CoreWebView2.DownloadStarting += async (_, args) => { using var deferral = args.GetDeferral(); args.Cancel = true; try { args.Cancel = !await owner.Confirm("Download file", Path.GetFileName(args.ResultFilePath) + " from " + args.DownloadOperation.Uri); } catch { args.Cancel = true; } };
            readyViews.Add(view);
            if (!builtInHome) view.Source = new Uri(url);
            if (Current == view) {
                UpdateAddress();
                if (builtInHome) search.Focus(FocusState.Programmatic);
            }
        } catch (Exception error) { UpdateAddress(); owner.Error(error); }
    }
    public void Dispose() { foreach (var view in views.Values) view.Close(); views.Clear(); readyViews.Clear(); pages.Clear(); pageHost.Children.Clear(); privateTabs.Clear(); tabs.TabItems.Clear(); }
}

