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
    public static void ApplyPalette(bool halloween)
    {
        var resources = Application.Current.Resources;
        var dark = (ResourceDictionary)resources.ThemeDictionaries["Default"];
        Set(dark, "KronoBackground", halloween ? "15101D" : "10151D");
        Set(dark, "KronoSidebar", halloween ? "1D1528" : "141B24");
        Set(dark, "KronoSurface", halloween ? "2B1D39" : "1B2532");
        Set(dark, "KronoBorder", halloween ? "442D56" : "2B3B4C");
        Set(dark, "ButtonBackgroundPointerOver", halloween ? "3B284B" : "293D48");
        Set(dark, "ButtonBackgroundPressed", halloween ? "503460" : "344D59");
        Set(resources, "AccentFillColorDefaultBrush", halloween ? "FF9B50" : "8FD7C7");
        Set(resources, "AccentFillColorSecondaryBrush", halloween ? "E88C48" : "76C6B5");
        Set(resources, "NavigationViewSelectionIndicatorForeground", halloween ? "FF9B50" : "8FD7C7");
        static void Set(ResourceDictionary dictionary, string key, string hex)
        {
            var color = Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            ((SolidColorBrush)dictionary[key]).Color = color;
        }
    }
    public static void ApplyTypography(Workspace workspace, FrameworkElement root)
    {
        var textName = workspace.Setting("native.textFont");
        var codeName = workspace.Setting("native.codeFont");
        var text = new FontFamily(textName.Length > 0 ? textName : "Segoe UI Variable Text");
        var code = new FontFamily(codeName.Length > 0 ? codeName : "Cascadia Code");
        var textSize = double.TryParse(workspace.Setting("native.textSize"), out var ts) ? Math.Clamp(ts, 11, 28) : 14;
        var codeSize = double.TryParse(workspace.Setting("native.codeSize"), out var cs) ? Math.Clamp(cs, 10, 26) : 14;
        Application.Current.Resources["KronoTextFont"] = text;
        Application.Current.Resources["KronoDisplayFont"] = new FontFamily(textName.Length > 0 ? textName : "Segoe UI Variable Display");
        Application.Current.Resources["KronoCodeFont"] = code;
        Application.Current.Resources["KronoTextSize"] = textSize;
        Application.Current.Resources["KronoCodeSize"] = codeSize;
        Refresh(root);
        void Refresh(DependencyObject element)
        {
            if (element is Control control) {
                var id = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control);
                var isCode = id is "TerminalInput" or "TerminalOutput";
                control.FontFamily = isCode ? code : text;
                if (control is TextBox or PasswordBox or ComboBox or NumberBox or Button) control.FontSize = isCode ? codeSize : textSize;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Refresh(VisualTreeHelper.GetChild(element, i));
        }
    }
    public static void Present(ContentControl host, FrameworkElement content)
    {
        if (ReferenceEquals(host.Content, content)) return;
        host.Content = content;
        if (!new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0); animation.InsertKeyFrame(1, 1);
        animation.Duration = PageMotion; visual.StartAnimation("Opacity", animation);
    }
}
