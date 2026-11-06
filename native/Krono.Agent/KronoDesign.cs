using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

// Shared visual roles for every native page; gameplay animation is separate.
public static class KronoDesign
{
    public static readonly TimeSpan PageMotion = TimeSpan.FromMilliseconds(160);
    public static FontFamily TextFont => (FontFamily)Application.Current.Resources["KronoTextFont"];
    public static double TextSize => (double)Application.Current.Resources["KronoTextSize"];
    public static FontFamily CodeFont => (FontFamily)Application.Current.Resources["KronoCodeFont"];
    public static double CodeSize => (double)Application.Current.Resources["KronoCodeSize"];
    public static FontFamily ResolveFont(string name) => new(name.Trim().ToLowerInvariant() switch {
        "rubik" => "ms-appx:///Assets/Fonts/Rubik.ttf#Rubik",
        "space grotesk" => "ms-appx:///Assets/Fonts/SpaceGrotesk.ttf#Space Grotesk",
        "jetbrains mono" => "ms-appx:///Assets/Fonts/JetBrainsMono.ttf#JetBrains Mono",
        _ => name.Trim()
    });
    static double ReadSize(string value, int minimum, int maximum) {
        var parsed = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || double.TryParse(value, out number);
        return parsed && double.IsFinite(number) ? Math.Clamp(number, minimum, maximum) : 14;
    }
    public static void ApplyPalette(bool halloween)
    {
        var resources = Application.Current.Resources;
        var dark = (ColorPaletteResources)resources.ThemeDictionaries["Default"];
        dark.Accent = Parse(halloween ? "F49750" : "51BB82");
        Set(dark, "KronoBackground", halloween ? "15101D" : "10151D");
        Set(dark, "KronoSidebar", halloween ? "1D1528" : "141B24");
        Set(dark, "KronoSurface", halloween ? "2B1D39" : "1B2532");
        Set(dark, "KronoBorder", halloween ? "442D56" : "2B3B4C");
        Set(dark, "KronoLoginBackdrop", halloween ? "1D1329" : "111D20");
        Set(dark, "KronoOutlineHover", halloween ? "A077A6" : "537D70");
        Set(dark, "ButtonBackgroundPointerOver", halloween ? "3B284B" : "293D48");
        Set(dark, "ButtonBackgroundPressed", halloween ? "503460" : "344D59");
        Set(dark, "KronoMuted", halloween ? "CEAEDF" : "9AABBF");
        Set(dark, "KronoAccentForeground", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "KronoAccentFill", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "KronoAccentHover", halloween ? "FFC590" : "A0F0C5");
        Set(dark, "KronoAccentPressed", halloween ? "F49750" : "70D39F");
        Set(dark, "AccentFillColorDefaultBrush", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "AccentFillColorSecondaryBrush", halloween ? "F49750" : "70D39F");
        Set(dark, "AccentFillColorTertiaryBrush", halloween ? "E88749" : "5CC08C");
        Set(dark, "AccentTextFillColorPrimaryBrush", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "AccentTextFillColorSecondaryBrush", halloween ? "FFC590" : "A0F0C5");
        Set(dark, "AccentTextFillColorTertiaryBrush", halloween ? "F49750" : "70D39F");
        Set(dark, "NavigationViewSelectionIndicatorForeground", halloween ? "FFAD62" : "84E6B1");
        static void Set(ResourceDictionary dictionary, string key, string hex)
        {
            ((SolidColorBrush)dictionary[key]).Color = Parse(hex);
        }
        static Windows.UI.Color Parse(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
    public static Brush ThemeBrush(string key, ElementTheme theme)
    {
        var dictionaries = Application.Current.Resources.ThemeDictionaries;
        return (Brush)((ResourceDictionary)dictionaries[theme == ElementTheme.Light ? "Light" : "Default"])[key];
    }
    public static void ApplyPrimaryStates(Button button)
    {
        button.Resources["AccentButtonBackgroundPointerOver"] = ThemeBrush("KronoAccentHover", ElementTheme.Dark);
        button.Resources["AccentButtonBackgroundPressed"] = ThemeBrush("KronoAccentPressed", ElementTheme.Dark);
    }
    public static void ApplyControlAccents(FrameworkElement root)
    {
        var resources = root.Resources;
        var ink = ThemeBrush("KronoAccentForeground", root.ActualTheme);
        var fill = ThemeBrush("KronoAccentFill", ElementTheme.Dark);
        var hover = ThemeBrush("KronoAccentHover", ElementTheme.Dark);
        var pressed = ThemeBrush("KronoAccentPressed", ElementTheme.Dark);
        var onAccent = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var surface = ThemeBrush("KronoSurface", root.ActualTheme);
        var outlineHover = ThemeBrush("KronoOutlineHover", root.ActualTheme);
        resources["TextControlThemeMinHeight"] = Application.Current.Resources["KronoControlMinHeight"];
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        resources["TextControlBorderBrushPointerOver"] = outlineHover;
        resources["ComboBoxBorderBrushPointerOver"] = outlineHover;
        resources["ComboBoxBackgroundBorderBrushFocused"] = ink;
        resources["ButtonBorderBrushPointerOver"] = outlineHover;
        resources["ButtonBorderBrushPressed"] = outlineHover;
        foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundFocused" })
            resources[key] = surface;

        foreach (var key in new[] { "TextControlBorderBrushFocused", "ListViewItemSelectionIndicatorBrush",
            "ListViewItemSelectionIndicatorPointerOverBrush", "ListViewItemSelectionIndicatorPressedBrush",
            "ProgressRingForegroundThemeBrush", "ProgressBarForeground", "HyperlinkForeground", "HyperlinkButtonForeground" })
            resources[key] = ink;
        foreach (var key in new[] { "TextControlSelectionHighlightColor", "AccentButtonBackground", "CheckBoxCheckBackgroundFillChecked",
            "CheckBoxCheckBackgroundStrokeChecked", "ToggleButtonBackgroundChecked" })
            resources[key] = fill;
        foreach (var key in new[] { "AccentButtonBackgroundPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "ToggleButtonBackgroundCheckedPointerOver" })
            resources[key] = hover;
        foreach (var key in new[] { "AccentButtonBackgroundPressed", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeCheckedPressed", "ToggleButtonBackgroundCheckedPressed" })
            resources[key] = pressed;
        foreach (var key in new[] { "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" })
            resources[key] = onAccent;
    }
    public static void ApplyTypography(Workspace workspace, FrameworkElement root)
    {
        var textName = workspace.Setting("native.textFont").Trim();
        var codeName = workspace.Setting("native.codeFont").Trim();
        var text = ResolveFont(textName.Length > 0 ? textName : "Rubik");
        var code = ResolveFont(codeName.Length > 0 ? codeName : "JetBrains Mono");
        var textSize = ReadSize(workspace.Setting("native.textSize"), 11, 28);
        var codeSize = ReadSize(workspace.Setting("native.codeSize"), 10, 26);
        Application.Current.Resources["KronoTextFont"] = text;
        Application.Current.Resources["ContentControlThemeFontFamily"] = text;
        Application.Current.Resources["KronoDisplayFont"] = ResolveFont(textName.Length == 0 || textName.Equals("Rubik", StringComparison.OrdinalIgnoreCase) ? "Space Grotesk" : textName);
        Application.Current.Resources["KronoCodeFont"] = code;
        Application.Current.Resources["KronoTextSize"] = textSize;
        Application.Current.Resources["KronoCodeSize"] = codeSize;
        RefreshTypography(MainWindow.Instance?.Content is DependencyObject windowRoot ? windowRoot : root);
    }
    static void RefreshTypography(DependencyObject root)
    {
        var text = TextFont; var code = CodeFont; var textSize = TextSize; var codeSize = CodeSize;
        Refresh(root);
        void Refresh(DependencyObject element, bool codeContext = false)
        {
            var isCode = codeContext;
            if (element is Control control) {
                var id = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control);
                var gameCode = control.Tag?.ToString() == "game-code";
                isCode |= id is "TerminalInput" or "TerminalOutput" or "ToolApprovalDetails" || gameCode;
                control.FontFamily = isCode ? code : text;
                if (!gameCode && !id.StartsWith("GameCell", StringComparison.Ordinal) && control is TextBox or PasswordBox or ComboBox or NumberBox or Button) control.FontSize = isCode ? codeSize : control.Tag?.ToString() == "caption" ? Math.Max(11, textSize - 3) : textSize;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Refresh(VisualTreeHelper.GetChild(element, i), isCode);
        }
    }
    public static void Present(ContentControl host, FrameworkElement content)
    {
        if (ReferenceEquals(host.Content, content)) { RefreshTypography(content); return; }
        host.Content = content;
        RefreshTypography(content);
        if (!new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0); animation.InsertKeyFrame(1, 1);
        animation.Duration = PageMotion; visual.StartAnimation("Opacity", animation);
    }
}
