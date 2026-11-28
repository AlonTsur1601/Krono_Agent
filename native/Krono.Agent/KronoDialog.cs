using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Krono_Agent;

internal sealed class KronoDialog : ContentDialog
{
    internal bool DestructivePrimary { get; init; }
    public KronoDialog()
    {
        if (MainWindow.Instance?.Content is FrameworkElement root)
            RequestedTheme = root.ActualTheme;
        var resources = Application.Current.Resources;
        PrimaryButtonStyle = (Style)resources["KronoPrimaryButton"];
        SecondaryButtonStyle = CloseButtonStyle = (Style)resources["KronoButton"];
        CornerRadius = new CornerRadius(18);
        Loaded += (_, _) => RefreshPalette();
        Opened += (_, _) => {
            if (MainWindow.Instance?.Content is FrameworkElement host) {
                void FollowTheme(FrameworkElement sender, object args) => RequestedTheme = sender.ActualTheme;
                RequestedTheme = host.ActualTheme;
                host.ActualThemeChanged += FollowTheme;
                Closed += DetachTheme;
                void DetachTheme(ContentDialog sender, ContentDialogClosedEventArgs args)
                {
                    host.ActualThemeChanged -= FollowTheme;
                    Closed -= DetachTheme;
                }
            }
            // WinUI hosts the dimmed background in a second popup behind the dialog.
            var overlay = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .Select(popup => popup.Child).OfType<Rectangle>().FirstOrDefault();
            if (overlay is not null) {
                PointerEventHandler dismiss = DismissFromOverlay;
                overlay.AddHandler(UIElement.PointerReleasedEvent, dismiss, true);
                Closed += DetachOverlay;
                void DetachOverlay(ContentDialog sender, ContentDialogClosedEventArgs args)
                {
                    overlay.RemoveHandler(UIElement.PointerReleasedEvent, dismiss);
                    Closed -= DetachOverlay;
                }
            }
            KronoDesign.RefreshTypography(this);
            if (GetTemplateChild("Title") is ContentControl title)
                title.FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["KronoDisplayFont"];
            if (GetTemplateChild("Content") is ContentPresenter content) {
                content.FontFamily = KronoDesign.TextFont;
                content.FontSize = KronoDesign.TextSize;
            }
        };
        ActualThemeChanged += (_, _) => RefreshPalette();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (DestructivePrimary && GetTemplateChild("PrimaryButton") is Button primary)
            KronoDesign.MarkDestructive(primary);
        if (GetTemplateChild("Container") is not FrameworkElement container) return;
        var defaults = VisualStateManager.GetVisualStateGroups(container)
            .FirstOrDefault(group => group.Name == "DefaultButtonStates");
        if (defaults is null) return;

        var activeState = defaults.CurrentState?.Name;
        VisualStateManager.GoToState(this, "NoDefaultButton", false);
        // WinUI substitutes the system accent style for the default action.
        // Keep its keyboard behavior while letting the three style bindings render the buttons.
        foreach (var state in defaults.States.Where(state => state.Name.EndsWith("AsDefaultButton", StringComparison.Ordinal))) {
            state.Setters.Clear();
        }
        if (activeState is not null) VisualStateManager.GoToState(this, activeState, false);
    }

    void DismissFromOverlay(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (!string.IsNullOrEmpty(CloseButtonText) && GetTemplateChild("CloseButton") is Button { IsEnabled: true } close &&
            FrameworkElementAutomationPeer.CreatePeerForElement(close)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            invoke.Invoke();
        else Hide();
    }

    void RefreshPalette()
    {
        Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
        BorderBrush = KronoDesign.ThemeBrush("KronoBorder", ActualTheme);
        KronoDesign.ApplyControlAccents(this);
    }
}
