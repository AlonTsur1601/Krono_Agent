using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

internal sealed class KronoDialog : ContentDialog
{
    public KronoDialog()
    {
        if (MainWindow.Instance?.Content is FrameworkElement root)
            RequestedTheme = root.ActualTheme;
        var resources = Application.Current.Resources;
        PrimaryButtonStyle = (Style)resources["KronoPrimaryButton"];
        SecondaryButtonStyle = CloseButtonStyle = (Style)resources["KronoGamePiece"];
        CornerRadius = new CornerRadius(18);
        Loaded += (_, _) => RefreshPalette();
        Opened += (_, _) => {
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
        if (GetTemplateChild("Container") is not FrameworkElement container) return;
        var defaults = VisualStateManager.GetVisualStateGroups(container)
            .FirstOrDefault(group => group.Name == "DefaultButtonStates");
        if (defaults is null) return;

        var activeState = defaults.CurrentState?.Name;
        VisualStateManager.GoToState(this, "NoDefaultButton", false);
        foreach (var state in defaults.States) {
            var style = state.Name switch {
                "PrimaryAsDefaultButton" => PrimaryButtonStyle,
                "SecondaryAsDefaultButton" => SecondaryButtonStyle,
                "CloseAsDefaultButton" => CloseButtonStyle,
                _ => null,
            };
            if (style is null) continue;
            foreach (var setter in state.Setters.OfType<Setter>()) setter.Value = style;
        }
        if (activeState is not null) VisualStateManager.GoToState(this, activeState, false);
    }

    void RefreshPalette()
    {
        Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
        BorderBrush = KronoDesign.ThemeBrush("KronoBorder", ActualTheme);
        KronoDesign.ApplyControlAccents(this);
    }
}
