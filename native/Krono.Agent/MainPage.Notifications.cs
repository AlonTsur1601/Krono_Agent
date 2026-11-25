using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly StackPanel notices = new() { Spacing = 8, IsHitTestVisible = false };
    readonly Dictionary<Border, DispatcherTimer> noticeTimers = [];
    Popup? noticePopup;

    void HideError()
    {
        foreach (var timer in noticeTimers.Values) timer.Stop();
        noticeTimers.Clear();
        notices.Children.Clear();
        CloseNoticePopup();
    }

    void CloseNoticePopup()
    {
        if (noticePopup is not { } popup) return;
        popup.XamlRoot.Changed -= PositionNotices;
        popup.IsOpen = false;
        popup.Child = null;
        noticePopup = null;
    }

    void PositionNotices(XamlRoot root, XamlRootChangedEventArgs? args = null)
    {
        if (noticePopup is not { } popup) return;
        notices.Width = Math.Clamp(root.Size.Width - 32, 0, 430);
        popup.HorizontalOffset = (root.Size.Width - notices.Width) / 2;
    }

    internal static string FriendlyError(Exception error)
    {
        if (error is OperationCanceledException) return "The operation was cancelled.";
        if (error is TimeoutException) return "The operation timed out. Try again.";
        if (error is System.Net.Http.HttpRequestException { StatusCode: { } status })
            return (int)status switch {
                401 or 403 => "Access denied. Check your credentials and permissions.",
                404 => "The requested resource is unavailable. Check the address or model.",
                429 => "The service is rate-limiting requests. Try again shortly.",
                >= 500 => "The service is temporarily unavailable. Try again shortly.",
                _ => "The service could not complete the request."
            };
        var message = error.Message.Trim();
        if (error is System.Text.Json.JsonException || message.Length == 0 || message.Contains('{') || message.StartsWith('[') ||
            message.Contains("Required at", StringComparison.OrdinalIgnoreCase))
            return "The service returned an unexpected response. Check the connection and try again.";
        return message.Length > 240 ? message[..239] + "…" : message;
    }

    public void Error(Exception error) => ShowNotice(FriendlyError(error), success: false);

    void ShowNotice(string message, bool success = true)
    {
        if (XamlRoot is not { } root) return;
        if (noticePopup is null) {
            noticePopup = new Popup { XamlRoot = root, Child = notices, VerticalOffset = 16, IsHitTestVisible = false };
            root.Changed += PositionNotices;
            PositionNotices(root);
        }
        if (notices.Children.Count >= 5 && notices.Children[0] is Border oldest) RemoveNotice(oldest);
        var ink = success ? new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(255, 23, 97, 62)
            : Windows.UI.Color.FromArgb(255, 132, 230, 177))
            : (SolidColorBrush)KronoDesign.ThemeBrush("KronoDangerForeground", ActualTheme);
        var accent = ink.Color;
        var background = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoSurface", ActualTheme)).Color;
        static byte Tint(byte surface, byte ink) => (byte)((surface * 4 + ink) / 5);
        var fill = Windows.UI.Color.FromArgb(255, Tint(background.R, accent.R), Tint(background.G, accent.G), Tint(background.B, accent.B));
        var label = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(label, success ? AutomationLiveSetting.Polite : AutomationLiveSetting.Assertive);
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new SymbolIcon(success ? Symbol.Accept : Symbol.Important) { Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var surface = new Border {
            Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 10, 16, 11),
            Background = new SolidColorBrush(fill), BackgroundSizing = BackgroundSizing.OuterBorderEdge,
            BorderBrush = ink, BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetAutomationId(label, success ? "SuccessNotice" : "ErrorNotice");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); AnimateNotice(surface, entering: false); };
        noticeTimers.Add(surface, timer);
        notices.Children.Add(surface);
        noticePopup.IsOpen = true;
        AnimateNotice(surface, entering: true);
        FrameworkElementAutomationPeer.CreatePeerForElement(label)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        timer.Start();
    }

    void RemoveNotice(Border surface)
    {
        if (!noticeTimers.Remove(surface, out var timer)) return;
        timer.Stop();
        notices.Children.Remove(surface);
        if (notices.Children.Count == 0) CloseNoticePopup();
    }

    void AnimateNotice(Border surface, bool entering)
    {
        if (!new UISettings().AnimationsEnabled) { if (!entering) RemoveNotice(surface); return; }
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, entering ? 0 : 1);
        fade.InsertKeyFrame(1, entering ? 1 : 0);
        fade.Duration = TimeSpan.FromMilliseconds(165);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new(0, entering ? -10 : 0, 0));
        slide.InsertKeyFrame(1, new(0, entering ? 0 : -10, 0));
        slide.Duration = fade.Duration;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => { if (!entering) RemoveNotice(surface); };
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
        batch.End();
    }
}
