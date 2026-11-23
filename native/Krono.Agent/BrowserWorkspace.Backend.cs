using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    bool firstBrowserControllerQueued;
    const string LinkChannel = "krono:link-preview:";
    const string LinkTrackingScript = """
        (() => {
            if (window.__kronoTracksLinks) return;
            window.__kronoTracksLinks = true;
            const channel = 'krono:link-preview:';
            const report = href => {
                const message = channel + href;
                if (window === top) window.chrome?.webview?.postMessage(message);
                else top.postMessage(message, '*');
            };
            if (window === top) addEventListener('message', ({data}) => {
                if (typeof data === 'string' && data.length <= 8192 && data.startsWith(channel))
                    window.chrome?.webview?.postMessage(data);
            });
            let hovered = null;
            addEventListener('pointerover', event => {
                const link = event.composedPath().find(node => node instanceof Element && node.matches('a[href], area[href]'));
                if (link === hovered) return;
                hovered = link;
                try { report(link ? new URL(link.getAttribute('href'), document.baseURI).href : ''); }
                catch { report(''); }
            }, true);
            addEventListener('pointerout', event => {
                if (!hovered || event.relatedTarget instanceof Node && hovered.contains(event.relatedTarget)) return;
                hovered = null;
                report('');
            }, true);
        })();
        """;

    void QueueFirstBrowserWarmup(WebView2 view, Func<Task> initialize, bool separateProfile)
    {
        if (firstBrowserControllerQueued || separateProfile || disposed) return;
        firstBrowserControllerQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () => {
            if (disposed || Current != view || !IsLoaded) {
                firstBrowserControllerQueued = false;
                return;
            }
            await initialize();
        });
    }

    void TrackBrowserLinkStatus(WebView2 view, CoreWebView2 core)
    {
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        // Only the top document talks to .NET. Subframes relay through the
        // document script, avoiding a native projection and subscription for each frame.
        core.WebMessageReceived += (_, args) => {
            if (Current != view || !readyViews.Contains(view)) return;
            string message;
            try { message = args.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; }
            if (message is null || message.Length > 8192 || !message.StartsWith(LinkChannel, StringComparison.Ordinal)) return;
            var text = message[LinkChannel.Length..];
            if (!Uri.TryCreate(text, UriKind.Absolute, out var destination) ||
                destination.Scheme is not ("http" or "https" or "mailto" or "tel" or "file")) text = "";
            if (linkDestination.Text != text) linkDestination.Text = text;
            linkStatus.MaxWidth = Math.Max(0, pageHost.ActualWidth - 16);
            linkStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };

        _ = RegisterBrowserLinkScript(view, core);
        core.DOMContentLoaded += async (_, _) => {
            // A cold controller can navigate before script registration completes.
            // The script guards against duplicate handlers when it was already injected.
            if (!readyViews.Contains(view)) return;
            BeginBrowserOperation(view);
            try { await core.ExecuteScriptAsync(LinkTrackingScript); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            finally { EndBrowserOperation(view); }
        };
    }

    async Task RegisterBrowserLinkScript(WebView2 view, CoreWebView2 core)
    {
        BeginBrowserOperation(view);
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(LinkTrackingScript); }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) {
            if (!disposed) App.RecordFault("Browser link preview", error);
        }
        finally { EndBrowserOperation(view); }
    }
}
