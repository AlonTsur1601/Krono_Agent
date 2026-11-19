using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed class KronoPressSurface : Grid
{
    public KronoPressSurface()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Loaded += (_, _) => {
            if (Children.OfType<Border>().FirstOrDefault(part => part.Name == "ButtonFace") is { } face)
                AttachPulse(this, face, 4);
        };
    }

    static readonly ConditionalWeakTable<UIElement, object> Pulses = new();

    internal static void AttachPulse(UIElement source, UIElement target, float depth)
    {
        if (!Pulses.TryAdd(target, new object())) return;
        ElementCompositionPreview.SetIsTranslationEnabled(target, true);
        var visual = ElementCompositionPreview.GetElementVisual(target);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new(.42f, 0), new(.58f, 1));
        var motion = compositor.CreateVector3KeyFrameAnimation();
        motion.Duration = TimeSpan.FromMilliseconds(200);
        motion.InsertExpressionKeyFrame(0, "this.StartingValue");
        motion.InsertKeyFrame(.44f, new System.Numerics.Vector3(0, depth, 0), ease);
        motion.InsertKeyFrame(1, System.Numerics.Vector3.Zero, ease);
        var settings = new UISettings();
        source.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => {
            var point = args.GetCurrentPoint(source);
            if (!point.Properties.IsLeftButtonPressed &&
                (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse || !args.Pointer.IsInContact)) return;
            if (settings.AnimationsEnabled) visual.StartAnimation("Translation", motion);
        }), true);
    }
}

public sealed class KronoTextBox : TextBox
{
    internal bool SurfaceFeedbackEnabled { get; init; } = true;
    internal bool PressMotionEnabled { get; init; } = true;
    public KronoTextBox()
    {
        KronoInputMotion.Attach(this, "KronoTextInput");
        VerticalContentAlignment = VerticalAlignment.Center;
    }
}

public sealed class KronoComboBox : ComboBox
{
    static readonly Dictionary<string, string> ItemPalette = new() {
        ["ComboBoxItemBackgroundPointerOver"] = "KronoSidebar",
        ["ComboBoxItemBackgroundPressed"] = "KronoInputFace",
        ["ComboBoxItemBackgroundSelected"] = "KronoSidebar",
        ["ComboBoxItemBackgroundSelectedUnfocused"] = "KronoSidebar",
        ["ComboBoxItemBackgroundSelectedPointerOver"] = "KronoInputFace",
        ["ComboBoxItemBackgroundSelectedPressed"] = "KronoInputFace",
        ["ComboBoxItemForegroundSelected"] = "KronoAccentForeground",
        ["ComboBoxItemForegroundSelectedUnfocused"] = "KronoAccentForeground",
        ["ComboBoxItemForegroundSelectedPointerOver"] = "KronoAccentForeground",
        ["ComboBoxItemForegroundSelectedPressed"] = "KronoAccentForeground",
        ["ComboBoxItemPillFillBrush"] = "KronoAccentForeground",
    };

    UIElement? outsideRoot;
    readonly PointerEventHandler outsidePress;

    void DetachOutsidePress()
    {
        outsideRoot?.RemoveHandler(UIElement.PointerPressedEvent, outsidePress);
        outsideRoot = null;
    }

    public KronoComboBox()
    {
        outsidePress = (_, args) => {
            var point = args.GetCurrentPoint(this).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
                IsDropDownOpen = false;
        };
        KronoInputMotion.Attach(this);
        Loaded += (_, _) => RefreshPalette();
        KronoInputMotion.RegisterPaletteRefresh(this, RefreshPalette);
        ActualThemeChanged += (_, _) => RefreshPalette();
        DropDownOpened += (_, _) => {
            RefreshPalette();
            if (GetTemplateChild("Popup") is not Popup dropdown) return;
            // WinUI inserts a canvas in front of the page to eat the first outside click.
            // Let the page receive that click and close the dropdown from the same event.
            if (dropdown.Child is Canvas canvas)
                foreach (var dismissLayer in canvas.Children.OfType<Canvas>()) dismissLayer.IsHitTestVisible = false;
            DependencyObject? scope = this;
            while (scope is not null && scope is not ContentDialog) scope = VisualTreeHelper.GetParent(scope);
            outsideRoot = scope as UIElement ?? XamlRoot?.Content;
            outsideRoot?.AddHandler(UIElement.PointerPressedEvent, outsidePress, true);
        };
        DropDownClosed += (_, _) => DetachOutsidePress();
        Unloaded += (_, _) => DetachOutsidePress();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("HighlightBackground") is Border highlight) {
            highlight.IsHitTestVisible = false;
            highlight.Margin = new Thickness(1);
            highlight.CornerRadius = new CornerRadius(Math.Max(0, CornerRadius.TopLeft - 1));
            highlight.BorderThickness = new Thickness(0);
            highlight.Background = null;
        }
        if (GetTemplateChild("PopupBorder") is Border popup) {
            popup.CornerRadius = new CornerRadius(16);
            popup.Padding = new Thickness(2);
            popup.BorderThickness = new Thickness(1, 1, 1, 3);
            if (GetTemplateChild("Popup") is Popup flyout && new UISettings().AnimationsEnabled)
                flyout.ChildTransitions = new TransitionCollection { new PopupThemeTransition() };
        }
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ComboBoxItem row) {
            row.CornerRadius = new CornerRadius(10);
            row.Margin = new Thickness(0);
            row.Padding = new Thickness(12, 7, 12, 7);
            row.MinHeight = 40;
            row.FontFamily = FontFamily;
            row.FontSize = FontSize;
        }
    }

    void RefreshPalette()
    {
        KronoDesign.ApplyControlAccents(this);
        var background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
        var border = KronoDesign.ThemeBrush("KronoBorder", ActualTheme);
        Resources["ComboBoxDropDownBackground"] = background;
        Resources["ComboBoxDropDownBorderBrush"] = border;
        if (new AccessibilitySettings().HighContrast) {
            foreach (var key in ItemPalette.Keys) Resources.Remove(key);
            return;
        }

        if (GetTemplateChild("EditableText") is TextBox editor) {
            editor.FontFamily = FontFamily;
            editor.FontSize = FontSize;
            editor.UseSystemFocusVisuals = false;
            editor.ApplyTemplate();
            KronoInputMotion.CenterTextContent(editor);
            foreach (var part in KronoInputMotion.Descendants(editor).OfType<Border>().Where(part => part.Name == "BorderElement"))
                part.Opacity = 0;
        }
        if (GetTemplateChild("PopupBorder") is Border popup) {
            popup.Background = background;
            popup.BorderBrush = border;
        }
        foreach (var (key, brush) in ItemPalette)
            Resources[key] = KronoDesign.ThemeBrush(brush, ActualTheme);
    }
}

internal static class KronoInputMotion
{
    static readonly ConditionalWeakTable<FrameworkElement, List<Action>> PaletteRefresh = new();
    internal static void RegisterPaletteRefresh(FrameworkElement element, Action refresh) =>
        PaletteRefresh.GetOrCreateValue(element).Add(refresh);

    internal static void RefreshPalette(FrameworkElement root)
    {
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Prepend(root))
            if (PaletteRefresh.TryGetValue(element, out var refreshes))
                foreach (var refresh in refreshes) refresh();
    }

    static readonly ConditionalWeakTable<Control, object> Attached = new();
    static readonly ConditionalWeakTable<ButtonBase, object> StyledButtons = new();
    static readonly ConditionalWeakTable<FrameworkElement, CompositeTransform> SurfaceTransforms = new();

    internal static void CenterTextContent(Control field)
    {
        foreach (var part in Descendants(field)) {
            if (part is ScrollViewer { Name: "ContentElement" } viewer)
                viewer.VerticalAlignment = VerticalAlignment.Center;
            else if (part is TextBlock { Name: "PlaceholderTextContentPresenter" } placeholder)
                placeholder.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    internal static void Attach(Control field, string? style = null, FrameworkElement? companion = null)
    {
        if (Attached.TryGetValue(field, out _)) return;
        Attached.Add(field, new object());
        if (style is not null) field.Style = (Style)Application.Current.Resources[style];
        Border? inputFace = null;
        SolidColorBrush? rim = null;
        GradientStop? shade = null, fill = null;
        bool hover = false, pressed = false;
        long pressStarted = 0;
        var pressFeedback = new DispatcherTimer();
        pressFeedback.Tick += (_, _) => { pressFeedback.Stop(); Refresh(); };
        void StyleParts() {
            if (field is KronoTextBox { SurfaceFeedbackEnabled: false }) return;
            field.ApplyTemplate();
            CenterTextContent(field);
            foreach (var child in Descendants(field).ToArray())
                if (child is ButtonBase button) StyleButton(button);
                else if (child is Border border) {
                    border.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(180) };
                }
            var faceName = field is KronoComboBox ? "Background" : "BorderElement";
            var face = Descendants(field).OfType<Border>().FirstOrDefault(border => border.Name == faceName);
            if (face is not null && face != inputFace && !new AccessibilitySettings().HighContrast && field.BorderThickness.Top > 0) {
                inputFace = face;
                shade = new GradientStop { Offset = 0 };
                fill = new GradientStop { Offset = 1 };
                rim = new SolidColorBrush();
                face.BorderBrush = rim;
                face.BackgroundSizing = BackgroundSizing.InnerBorderEdge;
                face.BackgroundTransition = null;
                face.Background = new LinearGradientBrush {
                    MappingMode = BrushMappingMode.Absolute, StartPoint = new(0, 0), EndPoint = new(0, 6),
                    GradientStops = { shade, fill }
                };
            }
            foreach (var element in Descendants(field).OfType<FrameworkElement>())
                foreach (var group in VisualStateManager.GetVisualStateGroups(element))
                {
                    if (rim is not null) {
                        foreach (var state in group.States) {
                            foreach (var setter in state.Setters.OfType<Setter>().Where(setter =>
                                setter.Target?.Path?.Path is "BorderElement.BorderBrush" or "Background.BorderBrush" or "BorderElement.Background" or "Background.Background").ToArray())
                                state.Setters.Remove(setter);
                            if (state.Storyboard is { } storyboard)
                                foreach (var animation in storyboard.Children.Where(animation => (Storyboard.GetTargetName(animation) is "BorderElement" or "Background") && (Storyboard.GetTargetProperty(animation) is "BorderBrush" or "Background")).ToArray())
                                    storyboard.Children.Remove(animation);
                        }
                    }
                    if (group.Name is "CommonStates" or "FocusStates" && group.Transitions.Count == 0)
                        group.Transitions.Add(new VisualTransition { GeneratedDuration = new Duration(TimeSpan.FromMilliseconds(180)) });
                }
            Refresh();
        }
        field.Loaded += (_, _) => StyleParts();
        if (field.IsLoaded) StyleParts();

        void Refresh()
        {
            if (field is KronoTextBox { SurfaceFeedbackEnabled: false }) return;
            var focused = field.FocusState != FocusState.Unfocused ||
                field is KronoComboBox { IsEditable: true } && Descendants(field).OfType<TextBox>().Any(editor => editor.FocusState != FocusState.Unfocused);
            var active = field.IsEnabled && (focused || hover);
            var pressing = field.IsEnabled && (pressed || pressFeedback.IsEnabled);
            AnimateSurface(field, pressing && field is not KronoTextBox { PressMotionEnabled: false } ? field is KronoComboBox ? .97f : .99f : 1, companion);
            if (rim is null || shade is null || fill is null) return;
            var theme = field.ActualTheme;
            shade.Color = ((SolidColorBrush)KronoDesign.ThemeBrush(field.IsEnabled ? "KronoInputShade" : "KronoInputFace", theme)).Color;
            fill.Color = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoInputFace", theme)).Color;
            var role = active ? (focused ? "KronoAccentForeground" : "KronoOutlineHover") : "KronoInputRim";
            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, rim, ((SolidColorBrush)KronoDesign.ThemeBrush(role, theme)).Color);
            storyboard.Begin();
        }
        field.PointerEntered += (_, _) => { hover = true; Refresh(); };
        void ReleasePress()
        {
            if (!pressed) return;
            pressed = false;
            // Keep quick clicks visible even when native focus and selection transfer pointer capture.
            var remaining = TimeSpan.FromMilliseconds(180) - System.Diagnostics.Stopwatch.GetElapsedTime(pressStarted);
            if (remaining > TimeSpan.Zero) { pressFeedback.Interval = remaining; pressFeedback.Start(); }
            Refresh();
        }
        field.PointerExited += (_, _) => { hover = false; ReleasePress(); Refresh(); };
        field.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => {
            if (!args.GetCurrentPoint(field).Properties.IsLeftButtonPressed && !args.Pointer.IsInContact) return;
            pressFeedback.Stop();
            pressStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            pressed = true;
            Refresh();
        }), true);
        field.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => ReleasePress()), true);
        field.PointerCaptureLost += (_, args) => {
            if (!args.GetCurrentPoint(field).Properties.IsLeftButtonPressed && !args.Pointer.IsInContact) ReleasePress();
        };
        field.GotFocus += (_, _) => field.DispatcherQueue.TryEnqueue(Refresh);
        field.ActualThemeChanged += (_, _) => field.DispatcherQueue.TryEnqueue(Refresh);
        RegisterPaletteRefresh(field, Refresh);
        field.LostFocus += (_, _) => field.DispatcherQueue.TryEnqueue(Refresh);
        field.IsEnabledChanged += (_, _) => { hover = pressed = false; pressFeedback.Stop(); Refresh(); };
        field.Unloaded += (_, _) => { hover = pressed = false; pressFeedback.Stop(); if (field is not KronoTextBox { SurfaceFeedbackEnabled: false }) AnimateSurface(field, 1, companion); };
    }

    internal static void StyleButton(ButtonBase button)
    {
        if (StyledButtons.TryGetValue(button, out _)) return;
        StyledButtons.Add(button, new object());
        if (button.Name == "CloseButton") {
            button.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Close tab");
            void SuppressHint()
            {
                if (ToolTipService.GetToolTip(button) is ToolTip tooltip) tooltip.IsEnabled = false;
                else if (ToolTipService.GetToolTip(button) is not null) ToolTipService.SetToolTip(button, null);
            }
            button.RegisterPropertyChangedCallback(ToolTipService.ToolTipProperty, (_, _) => SuppressHint());
            SuppressHint();
        }
        if (button.Name is "DeleteButton" or "PasswordVisibility" or "RevealButton" or "CloseButton") {
            if (button.Name is "DeleteButton" or "CloseButton")
                button.Content = new FontIcon { Glyph = "\uE894", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 };
            if (button.Name == "RevealButton" && button is ToggleButton eye) {
                var icon = new FontIcon { Glyph = "\uE890", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 };
                eye.Content = icon;
                void RefreshEye()
                {
                    icon.Glyph = eye.IsChecked == true ? "\uED1A" : "\uE890";
                    var label = eye.IsChecked == true ? "Hide password" : "Show password";
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(eye, label);
                    ToolTipService.SetToolTip(eye, label);
                }
                eye.Checked += (_, _) => RefreshEye();
                eye.Unchecked += (_, _) => RefreshEye();
                RefreshEye();
            }
            button.Width = button.Name == "CloseButton" ? 28 : 34;
            button.Height = 28;
            if (button.Name != "CloseButton") button.Margin = new Thickness(0, 0, 5, 0);
        }
        button.CornerRadius = new CornerRadius(6);
        button.MinWidth = button.MinHeight = 0;
        button.MaxHeight = 28;
        button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Template = (ControlTemplate)Application.Current.Resources["KronoPressTemplate"];
        void RefreshPalette()
        {
            button.Background = KronoDesign.ThemeBrush("KronoInputFace", button.ActualTheme);
            button.BorderBrush = KronoDesign.ThemeBrush("KronoBorder", button.ActualTheme);
        }
        RefreshPalette();
        button.ActualThemeChanged += (_, _) => RefreshPalette();
        RegisterPaletteRefresh(button, RefreshPalette);
    }

    static void AnimateSurface(Control field, float scale, FrameworkElement? companion)
    {
        if (VisualTreeHelper.GetChildrenCount(field) == 0 || VisualTreeHelper.GetChild(field, 0) is not Grid root) return;
        // The native templates put the header in row 0, input in row 1, and description in row 2.
        var surfaces = root.Children.OfType<FrameworkElement>().Where(element => Grid.GetRow(element) == 1 && element is not Popup).ToArray();
        var face = surfaces.FirstOrDefault(element => element.Name is "BorderElement" or "Background" or "InputBox");
        if (face is null) return;
        var center = face.ActualOffset + new System.Numerics.Vector3(face.ActualSize / 2, 0);
        var animationsEnabled = new UISettings().AnimationsEnabled;
        if (!animationsEnabled) scale = 1;
        var duration = new Duration(TimeSpan.FromMilliseconds(animationsEnabled ? 180 : 0));
        var storyboard = new Storyboard();
        foreach (var element in companion is null ? surfaces : surfaces.Append(companion)) {
            var transform = SurfaceTransforms.GetValue(element, element => {
                var motion = new CompositeTransform();
                var group = new TransformGroup();
                if (element.RenderTransform is { } original) group.Children.Add(original);
                group.Children.Add(motion);
                element.RenderTransform = group;
                return motion;
            });
            var origin = element == companion ? center + field.ActualOffset : center;
            transform.CenterX = origin.X - element.ActualOffset.X;
            transform.CenterY = origin.Y - element.ActualOffset.Y;
            foreach (var property in new[] { "ScaleX", "ScaleY" }) {
                var animation = new DoubleAnimation { To = scale, Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, EnableDependentAnimation = true };
                Storyboard.SetTarget(animation, transform);
                Storyboard.SetTargetProperty(animation, property);
                storyboard.Children.Add(animation);
            }
        }
        storyboard.Begin();
    }

    static void AddColorAnimation(Storyboard storyboard, DependencyObject target, Windows.UI.Color color)
    {
        var animation = new ColorAnimation { To = color, EnableDependentAnimation = true,
            Duration = new Duration(TimeSpan.FromMilliseconds(new UISettings().AnimationsEnabled ? 180 : 0)) };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Color");
        storyboard.Children.Add(animation);
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
