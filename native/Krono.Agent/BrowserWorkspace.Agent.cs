using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    WebView2? agentView;
    AgentNavigation? agentNavigation;
    ulong? agentPageLoading;
    int agentPageVersion;
    bool agentPageReady;
    sealed class AgentNavigation(Uri destination)
    {
        public Uri Destination { get; } = destination;
        public ulong? Id { get; set; }
        public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    readonly SemaphoreSlim agentAction = new(1);
    bool ShareAgentProfile => workspace.Setting("native.agentBrowserSharesProfile") == "true";
    string AgentUrlSetting => ShareAgentProfile ? "native.sharedAgentBrowserUrl" : "native.agentBrowserUrl";

    internal static bool CanAgentBrowse(Uri url)
        => (url.Scheme is "http" or "https") && string.IsNullOrEmpty(url.UserInfo) &&
            !string.IsNullOrWhiteSpace(url.Host) && url.AbsoluteUri.Length <= 4096;

    void AgentPageStarting(ulong id, Uri destination)
    {
        agentPageLoading = id;
        agentPageReady = false;
        agentPageVersion++;
        if (agentNavigation is not { } pending) return;
        if (pending.Id is null && pending.Destination == destination) pending.Id = id;
        else if (pending.Id is not null && pending.Id != id) pending.Result.TrySetResult(false);
    }

    void AgentPageFinished(ulong id, bool success)
    {
        if (agentPageLoading == id) {
            agentPageLoading = null;
            agentPageReady = success;
        }
        if (agentNavigation is { } pending && pending.Id == id) pending.Result.TrySetResult(success);
    }

    void ResetAgentPage()
    {
        agentPageLoading = null;
        agentPageReady = false;
        agentPageVersion++;
        agentNavigation?.Result.TrySetResult(false);
    }

    internal void SelectAgentTab()
    {
        if (agentView is { } view) tabs.SelectedItem = views.First(tab => ReferenceEquals(tab.Value, view)).Key;
    }

    async Task RestoreAgentTab()
    {
        if (agentView is not null) { SelectAgentTab(); return; }
        var previous = workspace.Setting(AgentUrlSetting);
        if (Uri.TryCreate(previous, UriKind.Absolute, out var url) && CanAgentBrowse(url)) await Open(url.AbsoluteUri, isAgent: true);
        else await Open(DefaultHome, isHome: true, isAgent: true);
    }

    internal async Task<object> RunAgentBrowser(JsonNode request)
    {
        static object Failed(string error) => new { success = false, error };
        if (disposed || !await WaitForBrowserHost()) return Failed("Open the Browser split to let the agent use its browser.");
        if (!await agentAction.WaitAsync(0)) return Failed("The agent browser is already opening a page.");
        try {
            var action = request["action"]?.ToString();
            if (action == "visit") {
                if (!Uri.TryCreate(request["url"]?.ToString(), UriKind.Absolute, out var url) || !CanAgentBrowse(url))
                    return Failed("Enter a valid HTTP or HTTPS page address.");
                if (!await NavigateAgentPage(url)) return Failed("The browser could not open that page.");
            } else if (agentView is null) {
                var previous = workspace.Setting(AgentUrlSetting);
                if (!Uri.TryCreate(previous, UriKind.Absolute, out var url) || !CanAgentBrowse(url) || !await NavigateAgentPage(url))
                    return Failed("The agent browser has no page open. Visit a page first.");
            }
            if (agentView is not { } view || !readyViews.Contains(view) || !agentPageReady)
                return Failed("The agent browser has no finished page to read.");
            if (action == "follow") {
                var label = request["link"]?.ToString();
                if (string.IsNullOrWhiteSpace(label) || label.Length > 180) return Failed("Use the exact visible link text.");
                var version = agentPageVersion;
                var link = await LocateAgentLink(view, label);
                if (link is null || !Uri.TryCreate(link["url"]?.ToString(), UriKind.Absolute, out var target) || !CanAgentBrowse(target))
                    return Failed("There is no unique link matching that text on this page.");
                if (version != agentPageVersion) return Failed("The page changed before the agent could follow that link.");
                await ShowAgentPointer(view, link["x"]?.GetValue<double>() ?? 0, link["y"]?.GetValue<double>() ?? 0);
                if (version != agentPageVersion || !await NavigateAgentPage(target)) return Failed("The browser could not follow that link.");
            } else if (action is not ("visit" or "read")) return Failed("That browser action is unavailable.");

            if (agentView is not { } loaded || !agentPageReady) return Failed("The browser tab closed or stopped.");
            var readVersion = agentPageVersion;
            var output = await ReadAgentPage(loaded);
            return readVersion == agentPageVersion ? new { success = true, output } : Failed("The page changed before it could be read.");
        } catch (TimeoutException) { return Failed("That page took too long to open."); }
        finally {
            agentNavigation = null;
            agentAction.Release();
        }
    }

    async Task<bool> WaitForBrowserHost()
    {
        if (IsLoaded) return true;
        var mounted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(object sender, RoutedEventArgs args) => mounted.TrySetResult();
        Loaded += Complete;
        try {
            await mounted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            return !disposed && IsLoaded;
        } catch (TimeoutException) { return false; }
        finally { Loaded -= Complete; }
    }

    async Task<bool> NavigateAgentPage(Uri target)
    {
        var loading = new AgentNavigation(target);
        if (agentView is { } view && readyViews.Contains(view) && navigators.TryGetValue(view, out var navigate)) {
            SelectAgentTab();
            if (view.Source == target && agentPageReady) return true;
            agentNavigation = loading;
            if (view.Source == target && agentPageLoading is { } id) loading.Id = id;
            else await navigate(target);
        } else {
            if (agentView is { } failed) CloseBrowserTab(views.First(tab => ReferenceEquals(tab.Value, failed)).Key);
            agentNavigation = loading;
            await Open(target.AbsoluteUri, isAgent: true);
        }
        return await loading.Result.Task.WaitAsync(TimeSpan.FromSeconds(35));
    }

    async Task<JsonNode?> LocateAgentLink(WebView2 view, string label)
    {
        var labelJson = JsonSerializer.Serialize(label);
        var result = await RunAgentScript(view, $$"""
            (() => {
                const label = {{labelJson}};
                const matches = Array.from(document.links).filter(link =>
                    link.innerText.trim().replace(/\s+/g, ' ') === label && link.getClientRects().length);
                if (matches.length !== 1) return null;
                const link = matches[0];
                link.scrollIntoView({ block: 'center', behavior: 'instant' });
                const box = link.getBoundingClientRect();
                return { url: link.href, x: box.left + box.width / 2, y: box.top + box.height / 2 };
            })()
            """);
        return JsonNode.Parse(result);
    }

    Task<string> ReadAgentPage(WebView2 view) => RunAgentScript(view, """
        (() => ({
            url: location.href,
            title: document.title.slice(0, 240),
            text: (document.body?.innerText || '').slice(0, 12000),
            links: Array.from(document.links).filter(link => link.getClientRects().length)
                .slice(0, 35).map(link => link.innerText.trim().replace(/\s+/g, ' ').slice(0, 180)).filter(Boolean)
        }))()
        """);

    async Task<string> RunAgentScript(WebView2 view, string script)
    {
        BeginBrowserOperation(view);
        try { return await view.ExecuteScriptAsync(script); }
        finally { EndBrowserOperation(view); }
    }

    async Task ShowAgentPointer(WebView2 view, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        var pointer = new FontIcon {
            Glyph = "\u27A4", FontSize = 20, IsHitTestVisible = false,
            Foreground = KronoDesign.ThemeBrush("KronoAccentForeground", ActualTheme),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Math.Clamp(x, 0, Math.Max(0, pageHost.ActualWidth - 22)), Math.Clamp(y, 0, Math.Max(0, pageHost.ActualHeight - 22)), 0, 0)
        };
        if (Current != view) return;
        pageHost.Children.Add(pointer);
        try { await Task.Delay(280); }
        finally { pageHost.Children.Remove(pointer); }
    }
}
