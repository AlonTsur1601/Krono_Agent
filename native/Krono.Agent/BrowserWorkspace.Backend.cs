using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    bool firstBrowserControllerQueued;

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
        core.StatusBarTextChanged += (_, _) => {
            if (Current != view || !readyViews.Contains(view)) return;
            var text = core.StatusBarText;
            SetLinkStatus(string.IsNullOrEmpty(text) || text.Length > 8192 ? "" : text);
        };
    }

    void SetLinkStatus(string text)
    {
        if (linkDestination.Text != text) {
            linkDestination.Text = text;
            AutomationProperties.SetItemStatus(address, text);
        }
        linkStatus.MaxWidth = Math.Max(0, pageHost.ActualWidth - 16);
        linkStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
