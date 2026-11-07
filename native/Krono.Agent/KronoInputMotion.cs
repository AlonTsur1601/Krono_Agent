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
    public KronoTextBox() => KronoInputMotion.Attach(this, "KronoTextInput");
}

public sealed class KronoComboBox : ComboBox
{
    public KronoComboBox() => KronoInputMotion.Attach(this);
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
                if (child is ButtonBase button) StyleButton(button);
                else if (child is Border border) {
                    border.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(180) };
                }
            if (highlight is null && Descendants(field).OfType<Border>().FirstOrDefault(border => border.Name == "BorderElement") is { Parent: Grid grid } face) {
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
            var active = field.IsEnabled && (field.FocusState != FocusState.Unfocused || hover);
            var pressing = field.IsEnabled && (pressed || pressFeedback.IsEnabled);
            AnimateSurface(field, pressing ? .99f : active ? 1.006f : 1, active && !pressing ? -1.5f : 0);
            if (highlight is null) return;
            var accent = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoAccentForeground", field.ActualTheme)).Color;
            accent.A = active ? (byte)(field.FocusState != FocusState.Unfocused ? 220 : 100) : (byte)0;
            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, highlight, accent);
            if (rim is not null) {
                var normal = (LinearGradientBrush)KronoDesign.ThemeBrush("KronoInputRim", field.ActualTheme);
                var blend = active ? (field.FocusState != FocusState.Unfocused ? .5 : .25) : 0;
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

    internal static void StyleButton(ButtonBase button)
    {
        if (StyledButtons.TryGetValue(button, out _)) return;
        StyledButtons.Add(button, new object());
        if (button.Name is "DeleteButton" or "PasswordVisibility") {
            if (button.Name == "DeleteButton")
                button.Content = new FontIcon { Glyph = "\uE894", FontSize = 14 };
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
        void Normal() { Paint(hover ? "ButtonBackgroundPointerOver" : "KronoInputFace"); Animate(button, hover ? .96f : 1); }
        Normal();
        button.ActualThemeChanged += (_, _) => Normal();
        button.PointerEntered += (_, _) => { hover = true; Normal(); };
        button.PointerExited += (_, _) => { hover = false; Normal(); };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { Paint("ButtonBackgroundPressed"); Animate(button, .88f); }), true);
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

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
