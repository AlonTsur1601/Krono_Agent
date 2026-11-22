using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly MainPage owner;
    readonly TabView tabs = new() { VerticalAlignment = VerticalAlignment.Stretch };
    readonly KronoTextBox address = new() { PlaceholderText = "Address or search", MinWidth = 120 };
    readonly Dictionary<TabViewItem, WebView2> views = new();
    readonly Dictionary<WebView2, Func<Uri, Task>> navigators = new();
    readonly Dictionary<TabViewItem, Grid> pages = new();
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
    readonly HashSet<WebView2> initializingViews = new();
    readonly Dictionary<WebView2, int> pendingBrowserOperations = new();
    readonly HashSet<WebView2> queuedCloses = new();
    bool disposed;
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
    public BrowserWorkspace(Workspace workspace, MainPage owner)
    {
        this.workspace = workspace; this.owner = owner;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(address, "BrowserAddress");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(linkDestination, "BrowserLinkDestination");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(ToolbarButton("Back", Symbol.Back, () => { if (Current is { } view && readyViews.Contains(view) && view.CanGoBack) view.GoBack(); }));
        bar.Children.Add(ToolbarButton("Forward", Symbol.Forward, () => { if (Current is { } view && readyViews.Contains(view) && view.CanGoForward) view.GoForward(); }));
        bar.Children.Add(ToolbarButton("Reload", Symbol.Refresh, () => {
            if (Current is { } view && readyViews.Contains(view) && view.Source is { Scheme: "http" or "https" })
                view.Reload();
        }));
        address.PreviewKeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Go(); } };
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
        AddMenuAction("Private tab", Symbol.Permissions, () => _ = Open(DefaultHome, isPrivate: true, isHome: true));
        AddMenuAction("Agent browser", Symbol.View, async () => await RestoreAgentTab());
        var shareProfile = new ToggleMenuFlyoutItem { Text = "Share my browser profile with the agent", IsChecked = ShareAgentProfile };
        shareProfile.Click += (_, _) => {
            workspace.SetSetting("native.agentBrowserSharesProfile", shareProfile.IsChecked ? "true" : "false");
            if (agentView is { } view) CloseBrowserTab(views.First(tab => ReferenceEquals(tab.Value, view)).Key);
            if (tabs.TabItems.Count == 0) _ = Open(DefaultHome, isHome: true);
        };
        menu.Items.Add(shareProfile);
        AddMenuAction("Find on page", Symbol.Find, async () => {
            var input = new KronoTextBox { PlaceholderText = "Find text" };
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
            if ((submittedFromField || result == ContentDialogResult.Primary) && Current is { } view && readyViews.Contains(view)) {
                try { await view.ExecuteScriptAsync("window.find(" + System.Text.Json.JsonSerializer.Serialize(input.Text) + ",false,false,true)"); }
                catch (Exception error) when (!disposed && readyViews.Contains(view)) { owner.Error(error); }
                catch (Exception) when (disposed || !readyViews.Contains(view)) { }
            }
        });
        AddMenuAction("Bookmark", Symbol.Favorite, () => {
            if (tabs.SelectedItem is TabViewItem tab && !privateTabs.Contains(tab) &&
                Current is { Source: { Scheme: "http" or "https" } uri } view)
                workspace.SetSetting("browser.bookmark." + uri.AbsoluteUri, SerializeVisit(uri, view.CoreWebView2.DocumentTitle));
        });
        AddMenuAction("History & bookmarks", Symbol.Library, OpenHistory);
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
        frame.Children.Add(KronoDesign.CreateTabStrip(tabs, () => _ = Open(DefaultHome, isHome: true), bar));
        frame.Children.Add(browserContent);
        Grid.SetRow(browserContent, 1);
        Children.Add(KronoDesign.FrameTabContent(frame, "KronoSidebar"));
        tabs.TabCloseRequested += (sender, args) => {
            if (args.Tab is not TabViewItem tab) return;
            CloseBrowserTab(tab);
            if (!disposed && tabs.TabItems.Count == 0) _ = Open(DefaultHome, isHome: true);
        };
        tabs.SelectionChanged += (_, _) => {
            if (activePage is not null) activePage.Visibility = Visibility.Collapsed;
            activePage = tabs.SelectedItem is TabViewItem selected ? pages.GetValueOrDefault(selected) : null;
            if (activePage is not null) activePage.Visibility = Visibility.Visible;
            linkStatus.Visibility = Visibility.Collapsed;
            UpdateAddress();
        };
        Loaded += async (_, _) => {
            if (disposed || tabs.TabItems.Count != 0) return;
            await Open(DefaultHome, isHome: true);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () => {
                if (disposed) return;
                try { await GetBrowserEnvironment(); }
                catch (Exception error) { App.RecordFault("Browser startup", error); }
            });
        };
    }

    Task<CoreWebView2Environment> GetBrowserEnvironment()
    {
        if (browserEnvironment is null or { IsFaulted: true } or { IsCanceled: true }) {
            var folder = Path.Combine(Workspace.Home, "native-browser", workspace.AccountId, "default");
            browserEnvironment = CoreWebView2Environment.CreateWithOptionsAsync(null, folder, new CoreWebView2EnvironmentOptions { Language = "en-US" }).AsTask();
        }
        return browserEnvironment;
    }
    void CloseBrowserTab(TabViewItem tab)
    {
        if (views.Remove(tab, out var view)) {
            if (ReferenceEquals(agentView, view)) {
                agentView = null;
                ResetAgentPage();
            }
            readyViews.Remove(view);
            navigators.Remove(view);
            if (view.CoreWebView2 is not null || initializingViews.Contains(view)) CloseViewWhenIdle(view);
        }
        if (pages.Remove(tab, out var page)) pageHost.Children.Remove(page);
        privateTabs.Remove(tab);
        tabs.TabItems.Remove(tab);
    }

    void CloseViewWhenIdle(WebView2 view)
    {
        if (initializingViews.Contains(view) || pendingBrowserOperations.ContainsKey(view) || !queuedCloses.Add(view)) return;
        // Close outside the WebView2 callback and after the XAML host has removed the view.
        if (!DispatcherQueue.TryEnqueue(() => {
            try { view.Close(); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            finally { queuedCloses.Remove(view); }
        })) queuedCloses.Remove(view);
    }

    void BeginBrowserOperation(WebView2 view)
        => pendingBrowserOperations[view] = pendingBrowserOperations.GetValueOrDefault(view) + 1;

    void EndBrowserOperation(WebView2 view)
    {
        if (--pendingBrowserOperations[view] != 0) return;
        pendingBrowserOperations.Remove(view);
        if (!views.ContainsValue(view)) CloseViewWhenIdle(view);
    }

    void QueueBrowserConfirmation(WebView2 view, Action completeDeferral, Func<Task> confirm)
    {
        BeginBrowserOperation(view);
        void Complete()
        {
            try { completeDeferral(); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            EndBrowserOperation(view);
        }
        if (!DispatcherQueue.TryEnqueue(async () => {
            try { if (!disposed && readyViews.Contains(view)) await confirm(); }
            catch (Exception error) { if (!disposed && readyViews.Contains(view)) owner.Error(error); }
            finally { Complete(); }
        })) Complete();
    }

    internal static IconElement BrowserTabIcon(bool isPrivate = false) => isPrivate
        ? new SymbolIcon(Symbol.Permissions)
        : new FontIcon { Glyph = "\uE774", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 16 };

    static (TextBlock Title, ContentControl Icon) CreateBrowserTabHeader(TabViewItem tab, string titleText, IconElement glyph)
    {
        var icon = new ContentControl { Content = glyph, Width = 18, Height = 18,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = titleText, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(icon);
        header.Children.Add(title);
        Grid.SetColumn(title, 1);
        KronoDesign.StyleTabHeader(tab);
        tab.HeaderTemplate = null;
        tab.Header = header;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, titleText);
        return (title, icon);
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
        address.IsEnabled = Current is { } view && navigators.ContainsKey(view);
    }
    async void Go()
    {
        var input = address.Text.Trim();
        if (input.Length == 0 || Current is not { } view || !navigators.TryGetValue(view, out var navigate)) return;
        try {
            if (!input.Contains(' ') && input.Contains('.') && !input.Contains("://")) input = "https://" + input;
            var url = Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : new Uri("https://www.google.com/search?hl=en&q=" + Uri.EscapeDataString(input));
            await navigate(url);
        } catch (Exception error) { owner.Error(error); }
    }
    async Task Open(string url, bool isPrivate = false, bool isHome = false, bool isAgent = false)
    {
        if (disposed) return;
        var view = new WebView2 { Visibility = Visibility.Collapsed };
        var tab = new TabViewItem();
        var tabName = isAgent ? "Agent browser" : isPrivate ? "Private tab" : "New tab";
        var start = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
        start.Children.Add(BrowserTabIcon(isPrivate));
        start.Children.Add(new TextBlock { Text = tabName, FontSize = 28, TextAlignment = TextAlignment.Center });
        var builtInHome = isHome && url == DefaultHome;
        var search = new KronoTextBox { PlaceholderText = "Search Google or enter an address", Width = 340, MaxWidth = 340,
            Visibility = builtInHome ? Visibility.Visible : Visibility.Collapsed };
        search.Loaded += (_, _) => {
            if (builtInHome && Current == view && search.Visibility == Visibility.Visible)
                search.Focus(FocusState.Programmatic);
        };
        search.PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Enter || Current != view ||
                string.IsNullOrWhiteSpace(search.Text)) return;
            args.Handled = true;
            address.Text = search.Text;
            Go();
        };
        var searchSlot = new Grid { MinHeight = 40 };
        searchSlot.Children.Add(search);
        var startStatus = new TextBlock { Text = "Opening page…", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Opacity = .72, MaxWidth = 320,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = builtInHome ? Visibility.Collapsed : Visibility.Visible };
        searchSlot.Children.Add(startStatus);
        start.Children.Add(searchSlot);
        var retry = new Button { Content = "Retry home page", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };

        start.Children.Add(retry);
        var content = new Grid { Visibility = Visibility.Collapsed };
        void Paint() => content.Background = KronoDesign.ThemeBrush("KronoSurface", content.ActualTheme);
        Paint();
        content.ActualThemeChanged += (_, _) => Paint();
        KronoInputMotion.RegisterPaletteRefresh(content, Paint);
        content.Children.Add(view); content.Children.Add(start);
        var (tabTitle, tabIcon) = CreateBrowserTabHeader(tab, tabName, BrowserTabIcon(isPrivate));
        if (isAgent) Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tab, "AgentBrowserTab");
        Uri? lastPage = null;
        Uri? queuedPage = null;
        Task? initialization = null;
        var restartRequired = false;
        ulong? navigationInProgress = null;
        async Task Navigate(Uri target)
        {
            if (!views.ContainsKey(tab)) return;
            if (restartRequired) {
                CloseBrowserTab(tab);
                await Open(target.AbsoluteUri, isPrivate, isAgent: isAgent);
                return;
            }
            if (readyViews.Contains(view)) {
                if (view.Source != target) view.Source = target;
                else if (navigationInProgress is null) view.Reload();
                return;
            }
            queuedPage = target;
            search.Visibility = Visibility.Collapsed;
            startStatus.Visibility = Visibility.Visible;
            startStatus.Text = "Opening page…";
            await (initialization ??= Initialize());
        }
        navigators.Add(view, Navigate);
        pages.Add(tab, content);
        pageHost.Children.Add(content);
        if (isAgent) agentView = view;
        views[tab] = view; if (isPrivate) privateTabs.Add(tab); tabs.TabItems.Add(tab); tabs.SelectedItem = tab;
        retry.Click += async (_, _) => { if ((lastPage ?? queuedPage) is { } target) await Navigate(target); };
        if (!builtInHome) await Navigate(new Uri(url));
        else if (Current == view && search.IsLoaded) search.Focus(FocusState.Programmatic);

        async Task Initialize()
        {
            initializingViews.Add(view);
            try {
                var environment = await GetBrowserEnvironment();
                if (!views.ContainsKey(tab)) return;
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = isPrivate;
                options.ProfileName = isPrivate ? "Private" : isAgent && !ShareAgentProfile ? "Agent" : "Default";
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
                int faviconGeneration = 0;
                void RefreshTabIcon()
                {
                    if (isHome) return;
                    _ = RefreshFavicon(view, tab, tabIcon, isPrivate, ++faviconGeneration, () => faviconGeneration);
                }
                view.CoreWebView2.ProcessFailed += (_, args) => {
                    var kind = args.ProcessFailedKind;
                    if (kind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited))
                        return;
                    DispatcherQueue.TryEnqueue(() => {
                        if (disposed || !views.ContainsKey(tab)) return;
                        restartRequired = true;
                        if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited) browserEnvironment = null;
                        readyViews.Remove(view);
                        if (isAgent) ResetAgentPage();
                        view.Visibility = Visibility.Collapsed;
                        start.Visibility = Visibility.Visible;
                        search.Visibility = Visibility.Collapsed;
                        startStatus.Visibility = Visibility.Visible;
                        startStatus.Text = "This browser tab stopped. Reopen it to continue.";
                        retry.Content = "Reopen tab";
                        retry.Visibility = Visibility.Visible;
                    });
                };
                void UpdateTabTitle()
                {
                    if (!views.ContainsKey(tab)) return;
                    var title = view.CoreWebView2.DocumentTitle;
                    tabTitle.Text = isHome || string.IsNullOrWhiteSpace(title) ? tabName : (isAgent ? "Agent · " : isPrivate ? "Private · " : "") + title;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, tabTitle.Text);
                }
                view.CoreWebView2.NavigationStarting += (_, args) => {
                    if (!views.ContainsKey(tab)) { args.Cancel = true; return; }
                    if (Current == view) linkStatus.Visibility = Visibility.Collapsed;
                    Uri.TryCreate(args.Uri, UriKind.Absolute, out var target);
                    if (isAgent) {
                        if (target is null || !CanAgentBrowse(target)) {
                            args.Cancel = true;
                            AgentPageFinished(args.NavigationId, false);
                            return;
                        }
                        AgentPageStarting(args.NavigationId, target);
                    }
                    navigationInProgress = args.NavigationId;
                    if (target?.Scheme is "http" or "https") {
                        isHome = false;
                        lastPage = target;
                        faviconGeneration++;
                        tabIcon.Content = BrowserTabIcon(isPrivate);
                    }
                    search.Visibility = Visibility.Collapsed;
                    startStatus.Visibility = Visibility.Visible;
                    if (view.Visibility != Visibility.Visible) {
                        startStatus.Text = "Opening page…";
                        retry.Visibility = Visibility.Collapsed;
                    }
                };
                string? visitKey = null;
                string? visitAddress = null;
                void UpdateVisit()
                {
                    if (views.ContainsKey(tab) && visitKey is not null && view.Source is { } uri && uri.AbsoluteUri == visitAddress)
                        workspace.SetSetting(visitKey, SerializeVisit(uri, view.CoreWebView2.DocumentTitle));
                }
                view.CoreWebView2.DocumentTitleChanged += (_, _) => { UpdateTabTitle(); UpdateVisit(); };
                view.CoreWebView2.FaviconChanged += (_, _) => RefreshTabIcon();

                view.CoreWebView2.NavigationCompleted += (sender, args) => {
                    if (!views.ContainsKey(tab)) return;
                    if (isAgent) AgentPageFinished(args.NavigationId, args.IsSuccess);
                    if (navigationInProgress != args.NavigationId) return;
                    navigationInProgress = null;
                    UpdateTabTitle();
                    if (Current == view && view.Source?.ToString() != "about:blank") address.Text = view.Source?.ToString() ?? "";
                    if (args.IsSuccess) {
                        start.Visibility = Visibility.Collapsed;
                        view.Visibility = Visibility.Visible;
                    } else {
                        view.Visibility = Visibility.Collapsed;
                        start.Visibility = Visibility.Visible;
                        startStatus.Text = "This page is unavailable. Try your home page again or enter another address.";
                        retry.Visibility = Visibility.Visible;
                    }
                    if (isAgent) {
                        if (args.IsSuccess && view.Source is { } destination && CanAgentBrowse(destination))
                            workspace.SetSetting(AgentUrlSetting, destination.AbsoluteUri);
                    }
                    if (args.IsSuccess && !isHome && !isPrivate && !isAgent && view.Source is { Scheme: "http" or "https" } visited) {
                        visitKey = "browser.history." + Workspace.Now + "." + Guid.NewGuid().ToString("N");
                        visitAddress = visited.AbsoluteUri;
                        UpdateVisit();
                    }
                };
                view.CoreWebView2.NewWindowRequested += (sender, args) => {
                    args.Handled = true;
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https")
                        DispatcherQueue.TryEnqueue(() => {
                            if (!views.ContainsKey(tab)) return;
                            if (!isAgent) _ = Open(target.AbsoluteUri, isPrivate);
                            else if (CanAgentBrowse(target)) _ = Navigate(target);
                        });
                };
                view.CoreWebView2.PermissionRequested += (_, args) => {
                    args.State = CoreWebView2PermissionState.Deny;
                    if (!views.ContainsKey(tab)) return;
                    var deferral = args.GetDeferral();
                    QueueBrowserConfirmation(view, deferral.Complete, async () => {
                        if (await owner.Confirm("Website permission", args.Uri + " requests " + args.PermissionKind) && readyViews.Contains(view))
                            args.State = CoreWebView2PermissionState.Allow;
                    });
                };
                view.CoreWebView2.DownloadStarting += (_, args) => {
                    args.Cancel = true;
                    if (!views.ContainsKey(tab)) return;
                    var deferral = args.GetDeferral();
                    QueueBrowserConfirmation(view, deferral.Complete, async () => {
                        if (await owner.Confirm("Download file", Path.GetFileName(args.ResultFilePath) + " from " + args.DownloadOperation.Uri) && readyViews.Contains(view))
                            args.Cancel = false;
                    });
                };
                readyViews.Add(view);
                if (queuedPage is { } target) view.Source = target;
            } catch (Exception error) {
                if (!disposed && views.ContainsKey(tab)) {
                    restartRequired = true;
                    startStatus.Text = "The browser could not open this tab.";
                    retry.Content = "Reopen tab";
                    retry.Visibility = Visibility.Visible;
                    App.RecordFault("Browser initialization", error);
                    if (isAgent) ResetAgentPage();
                }
            } finally {
                initializingViews.Remove(view);
                if (!views.ContainsKey(tab)) CloseViewWhenIdle(view);
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var tab in tabs.TabItems.OfType<TabViewItem>().ToArray()) CloseBrowserTab(tab);
    }
}
