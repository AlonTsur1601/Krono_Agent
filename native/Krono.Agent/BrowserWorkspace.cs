using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace Krono_Agent;

public sealed class BrowserWorkspace : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly MainPage owner;
    readonly TabView tabs = new() { IsAddTabButtonVisible = true };
    readonly TextBox address = new() { PlaceholderText = "Address or search", MinWidth = 240 };
    readonly AppBarButton go;
    readonly Dictionary<TabViewItem, WebView2> views = new();
    readonly HashSet<TabViewItem> privateTabs = new();
    WebView2? Current => tabs.SelectedItem is TabViewItem tab ? views.GetValueOrDefault(tab) : null;
    public BrowserWorkspace(Workspace workspace, MainPage owner)
    {
        this.workspace = workspace; this.owner = owner;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(address, "BrowserAddress");
        RowSpacing = 12; RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        var bar = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right };
        bar.PrimaryCommands.Add(Action("Back", Symbol.Back, () => { if (Current?.CanGoBack == true) Current.GoBack(); }));
        bar.PrimaryCommands.Add(Action("Forward", Symbol.Forward, () => { if (Current?.CanGoForward == true) Current.GoForward(); }));
        bar.PrimaryCommands.Add(Action("Reload", Symbol.Refresh, () => Current?.Reload()));
        bar.Content = address; address.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Go(); };
        go = Action("Go", Symbol.Go, Go); bar.PrimaryCommands.Add(go);
        bar.SecondaryCommands.Add(Action("Private tab", Symbol.Permissions, () => _ = Open("about:blank", true)));
        bar.SecondaryCommands.Add(Action("Find on page", Symbol.Find, async () => {
            var input = new TextBox { PlaceholderText = "Find text" };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Find on page", Content = input, PrimaryButtonText = "Find next", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && Current is { } view) await view.ExecuteScriptAsync("window.find(" + System.Text.Json.JsonSerializer.Serialize(input.Text) + ",false,false,true)");
        }));
        bar.SecondaryCommands.Add(Action("Bookmark", Symbol.Favorite, () => { if (tabs.SelectedItem is TabViewItem tab && !privateTabs.Contains(tab)) workspace.SetSetting("browser.bookmark." + (Current?.Source?.ToString() ?? ""), Current?.Source?.ToString() ?? ""); }));
        bar.SecondaryCommands.Add(Action("History & bookmarks", Symbol.Library, async () => {
            var list = new ListView { ItemsSource = workspace.Query("SELECT value FROM settings WHERE key LIKE 'browser.history.%' OR key LIKE 'browser.bookmark.%' ORDER BY key DESC LIMIT 100").Select(r => r["value"]!.ToString()).ToArray(), MaxHeight = 400 };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "History & bookmarks", Content = list, PrimaryButtonText = "Open selected", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is string url) await Open(url);
        }));
        bar.SecondaryCommands.Add(Action("Open externally", Symbol.OpenWith, () => { if (Current?.Source is Uri uri && uri.Scheme is "https" or "http") Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }));
        bar.SecondaryCommands.Add(Action("Zoom in", Symbol.ZoomIn, () => _ = Current?.ExecuteScriptAsync("document.body.style.zoom=(parseFloat(document.body.style.zoom)||1)+0.1")));
        bar.SecondaryCommands.Add(Action("Zoom out", Symbol.ZoomOut, () => _ = Current?.ExecuteScriptAsync("document.body.style.zoom=Math.max(.3,(parseFloat(document.body.style.zoom)||1)-0.1)")));
        Children.Add(bar); Grid.SetRow(tabs, 1); Children.Add(tabs);
        tabs.AddTabButtonClick += (_, _) => _ = Open("about:blank");
        tabs.TabCloseRequested += (_, args) => { if (args.Tab is TabViewItem tab) { views[tab].Close(); views.Remove(tab); privateTabs.Remove(tab); tabs.TabItems.Remove(tab); } };
        tabs.SelectionChanged += (_, _) => address.Text = Current?.Source?.ToString() ?? "";
        Loaded += async (_, _) => { if (tabs.TabItems.Count == 0) await Open("about:blank"); };
    }
    static AppBarButton Action(string label, Symbol symbol, Action action) { var button = new AppBarButton { Label = label, Icon = new SymbolIcon(symbol) }; button.Click += (_, _) => action(); return button; }
    void Go() { try { var input = address.Text.Trim(); var url = Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? input : "https://www.google.com/search?q=" + Uri.EscapeDataString(input); if (Current is { } view) view.Source = new Uri(url); } catch (Exception error) { owner.Error(error); } }
    async Task Open(string url, bool isPrivate = false)
    {
        try {
            var view = new WebView2(); var tab = new TabViewItem { Header = isPrivate ? "Private tab" : "New tab", Content = view };
            address.IsEnabled = false; go.IsEnabled = false;
            views[tab] = view; if (isPrivate) privateTabs.Add(tab); tabs.TabItems.Add(tab); tabs.SelectedItem = tab;
            var folder = Path.Combine(Workspace.Home, "native-browser", workspace.AccountId, "default");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, null);
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = isPrivate;
            options.ProfileName = isPrivate ? "Private" : "Default";
            await view.EnsureCoreWebView2Async(environment, options);
            view.CoreWebView2.DocumentTitleChanged += (_, _) => tab.Header = (isPrivate ? "Private · " : "") + view.CoreWebView2.DocumentTitle;
            view.CoreWebView2.NavigationCompleted += (_, args) => {
                if (Current == view && view.Source?.ToString() != "about:blank") address.Text = view.Source?.ToString() ?? "";
                if (!args.IsSuccess) owner.Error(new InvalidOperationException("Browser navigation failed: " + args.WebErrorStatus));
                if (!isPrivate && view.Source?.Scheme is "http" or "https") workspace.SetSetting("browser.history." + Workspace.Now, view.Source.ToString());
            };
            view.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https") _ = Open(args.Uri, isPrivate); };
            view.CoreWebView2.PermissionRequested += async (_, args) => { using var deferral = args.GetDeferral(); args.State = CoreWebView2PermissionState.Deny; try { if (await owner.Confirm("Website permission", args.Uri + " requests " + args.PermissionKind)) args.State = CoreWebView2PermissionState.Allow; } catch { args.State = CoreWebView2PermissionState.Deny; } };
            view.CoreWebView2.DownloadStarting += async (_, args) => { using var deferral = args.GetDeferral(); args.Cancel = true; try { args.Cancel = !await owner.Confirm("Download file", Path.GetFileName(args.ResultFilePath) + " from " + args.DownloadOperation.Uri); } catch { args.Cancel = true; } };
            view.Source = new Uri(url);
            address.IsEnabled = true; go.IsEnabled = true;
        } catch (Exception error) { address.IsEnabled = true; go.IsEnabled = true; owner.Error(error); }
    }
    public void Dispose() { foreach (var view in views.Values) view.Close(); views.Clear(); tabs.TabItems.Clear(); }
}

