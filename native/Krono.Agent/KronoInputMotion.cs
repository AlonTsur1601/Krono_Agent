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

public sealed class KronoTextBox : TextBox
{
    internal bool PressMotionEnabled { get; init; } = true;
    public KronoTextBox()
    {
        KronoInputMotion.Attach(this, "KronoTextInput");
        UpdateContentAlignment();
        RegisterPropertyChangedCallback(AcceptsReturnProperty, (_, _) => UpdateContentAlignment());
        RegisterPropertyChangedCallback(TextWrappingProperty, (_, _) => UpdateContentAlignment());
    }

    void UpdateContentAlignment() =>
        VerticalContentAlignment = AcceptsReturn || TextWrapping != TextWrapping.NoWrap
            ? VerticalAlignment.Top
            : VerticalAlignment.Center;
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

    public KronoComboBox()
    {
        KronoInputMotion.Attach(this);
        Loaded += (_, _) => RefreshPalette();
        ActualThemeChanged += (_, _) => RefreshPalette();
        DropDownOpened += (_, _) => RefreshPalette();
        GotFocus += (_, _) => RefreshEditableFocus(true);
        LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => RefreshEditableFocus(
            FocusState != FocusState.Unfocused || GetTemplateChild("EditableText") is TextBox { FocusState: not FocusState.Unfocused }));
    }

    void RefreshEditableFocus(bool focused)
    {
        if (!IsEditable || new AccessibilitySettings().HighContrast || GetTemplateChild("Background") is not Border face) return;
        var rim = (LinearGradientBrush)KronoDesign.ThemeBrush("KronoInputRim", ActualTheme);
        if (!focused) {
            face.BorderBrush = rim;
            return;
        }
        var accent = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoAccentForeground", ActualTheme)).Color;
        var focusedRim = new LinearGradientBrush { StartPoint = rim.StartPoint, EndPoint = rim.EndPoint };
        foreach (var stop in rim.GradientStops) {
            var color = stop.Color;
            color.R = (byte)((color.R + accent.R) / 2);
            color.G = (byte)((color.G + accent.G) / 2);
            color.B = (byte)((color.B + accent.B) / 2);
            focusedRim.GradientStops.Add(new GradientStop { Color = color, Offset = stop.Offset });
        }
        face.BorderBrush = focusedRim;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("HighlightBackground") is Border highlight) {
            highlight.Margin = new Thickness(1);
            highlight.CornerRadius = new CornerRadius(Math.Max(0, CornerRadius.TopLeft - 1));
            highlight.BorderThickness = new Thickness(1);
            highlight.Background = null;
        }
        if (GetTemplateChild("PopupBorder") is Border popup) {
            popup.CornerRadius = new CornerRadius(16);
            popup.Padding = new Thickness(6);
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
            row.Margin = new Thickness(0, 2, 0, 2);
            row.Padding = new Thickness(12, 7, 12, 7);
            row.MinHeight = 34;
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
    static readonly ConditionalWeakTable<Control, object> Attached = new();
    static readonly ConditionalWeakTable<ButtonBase, object> StyledButtons = new();
    static readonly ConditionalWeakTable<FrameworkElement, CompositeTransform> SurfaceTransforms = new();

    internal static void Attach(Control field, string? style = null)
    {
        if (Attached.TryGetValue(field, out _)) return;
        Attached.Add(field, new object());
        if (style is not null) field.Style = (Style)Application.Current.Resources[style];
        SolidColorBrush? highlight = null;
        LinearGradientBrush? rim = null;
        bool hover = false, pressed = false;
        long pressStarted = 0;
        var pressFeedback = new DispatcherTimer();
        pressFeedback.Tick += (_, _) => { pressFeedback.Stop(); Refresh(); };
        void StyleParts() {
            field.ApplyTemplate();
            foreach (var child in Descendants(field).ToArray())
                if (child is ButtonBase button) StyleButton(button, field is not KronoTextBox { PressMotionEnabled: false });
                else if (child is Border border) {
                    border.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(180) };
                }
            if (highlight is null && field is not KronoComboBox && Descendants(field).OfType<Border>().FirstOrDefault(border => border.Name == "BorderElement") is { Parent: Grid grid } face) {
                rim = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
                foreach (var stop in ((LinearGradientBrush)KronoDesign.ThemeBrush("KronoInputRim", field.ActualTheme)).GradientStops)
                    rim.GradientStops.Add(new GradientStop { Color = stop.Color, Offset = stop.Offset });
                face.BorderBrush = rim;
                highlight = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                var outline = new Border { BorderBrush = highlight, BorderThickness = new Thickness(1.5), CornerRadius = field.CornerRadius,
                    IsHitTestVisible = false, Margin = face.Margin };
                Grid.SetRow(outline, Grid.GetRow(face));
                Grid.SetColumn(outline, Grid.GetColumn(face));
                Grid.SetRowSpan(outline, Grid.GetRowSpan(face));
                Grid.SetColumnSpan(outline, Grid.GetColumnSpan(face));
                grid.Children.Add(outline);
            }
            foreach (var element in Descendants(field).OfType<FrameworkElement>())
                foreach (var group in VisualStateManager.GetVisualStateGroups(element))
                {
                    if (rim is not null)
                        foreach (var state in group.States)
                            if (state.Storyboard is { } storyboard)
                                foreach (var animation in storyboard.Children.Where(animation => Storyboard.GetTargetName(animation) == "BorderElement" && Storyboard.GetTargetProperty(animation) == "BorderBrush").ToArray())
                                    storyboard.Children.Remove(animation);
                    if (group.Name is "CommonStates" or "FocusStates" && group.Transitions.Count == 0)
                        group.Transitions.Add(new VisualTransition { GeneratedDuration = new Duration(TimeSpan.FromMilliseconds(180)) });
                }
            Refresh();
        }
        field.Loaded += (_, _) => StyleParts();
        if (field.IsLoaded) StyleParts();

        void Refresh()
        {
            var focused = field.FocusState != FocusState.Unfocused ||
                field is KronoComboBox { IsEditable: true } && Descendants(field).OfType<TextBox>().Any(editor => editor.FocusState != FocusState.Unfocused);
            var active = field.IsEnabled && (focused || hover);
            var pressing = field.IsEnabled && (pressed || pressFeedback.IsEnabled);
            AnimateSurface(field, pressing && field is not KronoTextBox { PressMotionEnabled: false } ? .99f : 1);
            if (highlight is null) return;
            var accent = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoAccentForeground", field.ActualTheme)).Color;
            accent.A = active ? (byte)(focused ? 220 : 100) : (byte)0;
            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, highlight, accent);
            if (rim is not null) {
                var normal = (LinearGradientBrush)KronoDesign.ThemeBrush("KronoInputRim", field.ActualTheme);
                var blend = active ? (focused ? .5 : .25) : 0;
                for (var i = 0; i < rim.GradientStops.Count; i++) {
                    var color = normal.GradientStops[i].Color;
                    color.R = (byte)(color.R + (accent.R - color.R) * blend);
                    color.G = (byte)(color.G + (accent.G - color.G) * blend);
                    color.B = (byte)(color.B + (accent.B - color.B) * blend);
                    AddColorAnimation(storyboard, rim.GradientStops[i], color);
                }
            }
            storyboard.Begin();
        }
        field.PointerEntered += (_, _) => { hover = true; Refresh(); };
        void ReleasePress()
        {
            if (!pressed) return;
            pressed = false;
            // Keep quick clicks visible even when native focus and selection transfer pointer capture.
            var remaining = TimeSpan.FromMilliseconds(90) - System.Diagnostics.Stopwatch.GetElapsedTime(pressStarted);
            if (remaining > TimeSpan.Zero) { pressFeedback.Interval = remaining; pressFeedback.Start(); }
            Refresh();
        }
        field.PointerExited += (_, _) => { hover = pressed = false; pressFeedback.Stop(); Refresh(); };
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
        field.ActualThemeChanged += (_, _) => Refresh();
        field.LostFocus += (_, _) => field.DispatcherQueue.TryEnqueue(Refresh);
        field.IsEnabledChanged += (_, _) => { hover = pressed = false; pressFeedback.Stop(); Refresh(); };
        field.Unloaded += (_, _) => { hover = pressed = false; pressFeedback.Stop(); AnimateSurface(field, 1); };
    }

    internal static void StyleButton(ButtonBase button, bool motionEnabled = true)
    {
        if (StyledButtons.TryGetValue(button, out _)) return;
        StyledButtons.Add(button, new object());
        if (button.Name is "DeleteButton" or "PasswordVisibility") {
            if (button.Name == "DeleteButton")
                button.Content = new FontIcon { Glyph = "\uE894", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 };
            button.Template = (ControlTemplate)Application.Current.Resources["KronoInputButtonTemplate"];
            button.Width = 34;
            button.Height = 28;
            button.Margin = new Thickness(0, 0, 5, 0);
        }
        button.CornerRadius = new CornerRadius(6);
        button.MinWidth = button.MinHeight = 0;
        button.MaxHeight = 28;
        button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        void Paint(string key) => button.Background = KronoDesign.ThemeBrush(key, button.ActualTheme);
        bool hover = false;
        void Normal() { Paint(hover ? "ButtonBackgroundPointerOver" : "KronoInputFace"); if (motionEnabled) Animate(button, hover ? .96f : 1); }
        Normal();
        button.ActualThemeChanged += (_, _) => Normal();
        button.PointerEntered += (_, _) => { hover = true; Normal(); };
        button.PointerExited += (_, _) => { hover = false; Normal(); };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { Paint("ButtonBackgroundPressed"); if (motionEnabled) Animate(button, .88f); }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Normal()), true);
        button.PointerCaptureLost += (_, _) => Normal();
    }

    static void AnimateSurface(Control field, float scale, float lift = 0)
    {
        if (VisualTreeHelper.GetChildrenCount(field) == 0 || VisualTreeHelper.GetChild(field, 0) is not Grid root) return;
        // The native templates put the header in row 0, input in row 1, and description in row 2.
        var surfaces = root.Children.OfType<FrameworkElement>().Where(element => Grid.GetRow(element) == 1 && element is not Popup).ToArray();
        var face = surfaces.FirstOrDefault(element => element.Name is "BorderElement" or "Background" or "InputBox");
        if (face is null) return;
        var center = face.ActualOffset + new System.Numerics.Vector3(face.ActualSize / 2, 0);
        var animationsEnabled = new UISettings().AnimationsEnabled;
        if (!animationsEnabled) { scale = 1; lift = 0; }
        var duration = new Duration(TimeSpan.FromMilliseconds(animationsEnabled ? 180 : 0));
        var storyboard = new Storyboard();
        foreach (var element in surfaces) {
            var transform = SurfaceTransforms.GetValue(element, element => {
                var motion = new CompositeTransform();
                var group = new TransformGroup();
                if (element.RenderTransform is { } original) group.Children.Add(original);
                group.Children.Add(motion);
                element.RenderTransform = group;
                return motion;
            });
            transform.CenterX = center.X - element.ActualOffset.X;
            transform.CenterY = center.Y - element.ActualOffset.Y;
            foreach (var (property, value) in new[] { ("ScaleX", (double)scale), ("ScaleY", (double)scale), ("TranslateY", (double)lift) }) {
                var animation = new DoubleAnimation { To = value, Duration = duration,
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

    static void Animate(FrameworkElement element, float scale, float lift = 0)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        if (!new UISettings().AnimationsEnabled) {
            visual.StopAnimation("Scale");
            visual.StopAnimation("Translation");
            visual.Scale = new(1, 1, 1);
            visual.Properties.InsertVector3("Translation", new(0, 0, 0));
            return;
        }
        visual.CenterPoint = new((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(180);
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, .8f), new(.2f, 1));
        animation.InsertKeyFrame(1, new(scale, scale, 1), easing);
        visual.StartAnimation("Scale", animation);
        var translation = visual.Compositor.CreateVector3KeyFrameAnimation();
        translation.Duration = animation.Duration;
        translation.InsertKeyFrame(1, new(0, lift, 0), easing);
        visual.StartAnimation("Translation", translation);
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
