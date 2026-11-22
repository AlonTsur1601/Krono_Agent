using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class MainPage
{
    Popup? errorPopup;
    Border? errorSurface;
    DispatcherTimer? errorTimer;
    int errorSequence;

    void HideError()
    {
        errorTimer?.Stop();
        if (errorPopup is not null) errorPopup.IsOpen = false;
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

    public void Error(Exception error)
    {
        if (XamlRoot is null) return;
        HideError();
        var label = new TextBlock { Text = FriendlyError(error), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Assertive);
        var surface = new Border {
            Child = label, CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 12, 18, 13),
            Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme),
            BorderBrush = KronoDesign.ThemeBrush("KronoBorder", ActualTheme), BorderThickness = new Thickness(1, 1, 1, 3),
            MaxWidth = Math.Max(140, Math.Min(430, ActualWidth - 32))
        };
        var popup = new Popup { XamlRoot = XamlRoot, Child = surface,
            HorizontalOffset = Math.Max(12, ActualWidth - surface.MaxWidth - 16), VerticalOffset = 16 };
        errorPopup = popup;
        errorSurface = surface;
        errorSequence++;
        popup.IsOpen = true;
        AnimateNotice(true);
        errorTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        errorTimer.Tick -= DismissError;
        errorTimer.Tick += DismissError;
        errorTimer.Start();
    }

    void DismissError(object? sender, object args)
    {
        errorTimer?.Stop();
        AnimateNotice(false);
    }

    void AnimateNotice(bool entering)
    {
        if (errorSurface is not { } surface || errorPopup is not { } popup) return;
        if (!new UISettings().AnimationsEnabled) { if (!entering) popup.IsOpen = false; return; }
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
        var sequence = errorSequence;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => { if (!entering && sequence == errorSequence) popup.IsOpen = false; };
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", slide);
        batch.End();
    }
}
