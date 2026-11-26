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
        void AlignText()
        {
            VerticalContentAlignment = AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center;
            if (IsLoaded) KronoInputMotion.AlignTextContent(this);
        }
        RegisterPropertyChangedCallback(AcceptsReturnProperty, (_, _) => AlignText());
        AlignText();
    }
}

public sealed class KronoCheckBox : CheckBox
{
    Border? backing;

    public KronoCheckBox()
    {
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => PaintBacking());
        IsEnabledChanged += (_, _) => PaintBacking();
        ActualThemeChanged += (_, _) => PaintBacking();
        KronoInputMotion.RegisterPaletteRefresh(this, PaintBacking);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (backing?.Parent is Grid oldHost) oldHost.Children.Remove(backing);
        backing = null;
        if (GetTemplateChild("NormalRectangle") is not Microsoft.UI.Xaml.Shapes.Rectangle { Parent: Grid host } face)
            return;

        face.RadiusX = face.RadiusY = CornerRadius.TopLeft;
        backing = new Border {
            Width = face.Width, Height = face.Height, CornerRadius = CornerRadius,
            HorizontalAlignment = face.HorizontalAlignment, VerticalAlignment = face.VerticalAlignment,
            Margin = face.Margin, RenderTransform = new TranslateTransform { Y = 3 },
            IsHitTestVisible = false
        };
        host.Children.Insert(0, backing);
        PaintBacking();
    }

    void PaintBacking()
    {
        if (backing is null) return;
        backing.Visibility = new AccessibilitySettings().HighContrast ? Visibility.Collapsed : Visibility.Visible;
        backing.Background = KronoDesign.ThemeBrush(!IsEnabled ? "KronoDisabledBase" :
            IsChecked != false ? "KronoPrimaryBase" : "KronoBorder", ActualTheme);
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
    Flyout? emptyMenu;
    Border? emptySurface;
    TextBlock? emptyMessage;
    string emptyText = "No options available.";
    internal string EmptyText
    {
        get => emptyText;
        set
        {
            emptyText = value;
            if (emptyMessage is not null) emptyMessage.Text = value;
        }
    }
    readonly PointerEventHandler outsidePress;

    void DetachOutsidePress()
    {
        outsideRoot?.RemoveHandler(UIElement.PointerPressedEvent, outsidePress);
        outsideRoot = null;
    }

    public KronoComboBox()
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Disabled);
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
            if (Items.Count == 0) {
                DispatcherQueue.TryEnqueue(ShowEmptyMenu);
                return;
            }
            if (GetTemplateChild("Popup") is not Popup dropdown) return;
            // WinUI inserts a canvas in front of the page to eat the first outside click.
            // Let the page receive that click and close the dropdown from the same event.
            if (dropdown.Child is Canvas canvas)
                foreach (var dismissLayer in canvas.Children.OfType<Canvas>()) dismissLayer.IsHitTestVisible = false;
            DependencyObject? scope = this;
            while (scope is not null && scope is not ContentDialog) scope = VisualTreeHelper.GetParent(scope);
            outsideRoot = scope as UIElement ?? XamlRoot?.Content;
            outsideRoot?.AddHandler(UIElement.PointerPressedEvent, outsidePress, true);
            if (GetTemplateChild("PopupBorder") is FrameworkElement surface) {
                surface.Width = ActualWidth;
                KronoDesign.AnimateMenuOpening(surface);
            }
        };
        Items.VectorChanged += (_, _) => { if (Items.Count != 0) emptyMenu?.Hide(); };
        DropDownClosed += (_, _) => {
            DetachOutsidePress();
            if (GetTemplateChild("PopupBorder") is Border popup) {
                KronoInputMotion.ResetFlatButtons(popup);
                foreach (var row in KronoInputMotion.Descendants(popup).OfType<ComboBoxItem>())
                    VisualStateManager.GoToState(row, row.IsSelected ? "Selected" : "Normal", false);
            }
        };
        Unloaded += (_, _) => { DetachOutsidePress(); emptyMenu?.Hide(); };
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
        if (GetTemplateChild("PopupBorder") is Border popup)
            popup.Style = (Style)Application.Current.Resources["KronoDropdownSurface"];
        if (GetTemplateChild("ScrollViewer") is ScrollViewer items)
            items.MinWidth = 0;
        RefreshPalette();
    }

    void ShowEmptyMenu()
    {
        if (!IsLoaded || !IsDropDownOpen || Items.Count != 0) return;
        // The system popup sizes itself against its item list, which has no height here.
        IsDropDownOpen = false;
        if (emptyMenu is null) {
            emptyMessage = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8, 12, 8) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(emptyMessage, "EmptyDropdownMessage");
            emptySurface = new Border { Child = emptyMessage, Style = (Style)Application.Current.Resources["KronoDropdownSurface"] };
            emptyMenu = new Flyout {
                Content = emptySurface, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                FlyoutPresenterStyle = (Style)Application.Current.Resources["KronoEmptyDropdownPresenter"]
            };
            emptyMenu.Opened += (_, _) => KronoDesign.AnimateMenuOpening(emptySurface);
        }
        emptySurface!.Width = ActualWidth;
        emptyMessage!.FontFamily = FontFamily;
        emptyMessage.FontSize = FontSize;
        emptyMessage.Text = EmptyText;
        RefreshPalette();
        emptyMenu.ShowAt(this);
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
            KronoInputMotion.AlignTextContent(editor);
            foreach (var part in KronoInputMotion.Descendants(editor).OfType<Border>().Where(part => part.Name == "BorderElement"))
                part.Opacity = 0;
        }
        if (GetTemplateChild("PopupBorder") is Border popup) {
            popup.Background = background;
            popup.BorderBrush = border;
        }
        if (emptySurface is not null) {
            emptySurface.Background = background;
            emptySurface.BorderBrush = border;
            emptyMessage!.Foreground = Foreground;
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
    static readonly ConditionalWeakTable<ButtonBase, Action> StyledButtons = new();

    internal static void ResetFlatButtons(DependencyObject root)
    {
        foreach (var button in Descendants(root).OfType<ButtonBase>())
            if (StyledButtons.TryGetValue(button, out var reset)) reset();
    }
    static readonly ConditionalWeakTable<FrameworkElement, CompositeTransform> SurfaceTransforms = new();

    internal static void AlignTextContent(Control field)
    {
        if (field is NumberBox && Descendants(field).OfType<TextBox>().FirstOrDefault() is { } editor) {
            editor.ApplyTemplate();
            AlignTextContent(editor);
            return;
        }
        var multiline = field is RichEditBox or TextBox { AcceptsReturn: true };
        foreach (var part in Descendants(field)) {
            if (part is ScrollViewer { Name: "ContentElement" } viewer) {
                // Center the native editor itself so its text and caret share the placeholder's baseline.
                viewer.VerticalAlignment = multiline ? VerticalAlignment.Stretch : VerticalAlignment.Center;
                viewer.VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
                if (viewer.Parent is Grid { ColumnDefinitions.Count: 2 } grid &&
                    grid.Children.OfType<ButtonBase>().FirstOrDefault(button => button.Name is "DeleteButton" or "RevealButton") is { } action) {
                    // Keep the action column on the physical right. The editor still handles bidi text itself.
                    grid.FlowDirection = FlowDirection.LeftToRight;
                    Grid.SetColumn(viewer, 0);
                    Grid.SetColumnSpan(viewer, 1);
                    Grid.SetColumn(action, 1);
                    action.HorizontalAlignment = HorizontalAlignment.Right;
                }
            } else if (part is FrameworkElement { Name: "PlaceholderTextContentPresenter" } placeholder)
                placeholder.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        }
    }

    internal static void Attach(Control field, string? style = null, FrameworkElement? companion = null, bool animatePress = true)
    {
        if (Attached.TryGetValue(field, out _)) return;
        Attached.Add(field, new object());
        if (style is not null) field.Style = (Style)Application.Current.Resources[style];
        Border? inputFace = null;
        SolidColorBrush? rim = null;
        GradientStop? shade = null, fill = null;
        (Windows.UI.Color Rim, Windows.UI.Color Shade, Windows.UI.Color Fill)? surfaceColors = null;
        (float Scale, float Lift)? surfaceMotion = null;
        bool hover = false, pressed = false;
        long pressStarted = 0;
        var pressFeedback = new DispatcherTimer();
        bool PressMotionEnabled() => animatePress && field is not KronoTextBox { PressMotionEnabled: false };
        bool IsLargeEditor() => field is (TextBox { AcceptsReturn: true } or RichEditBox)
            && (!PressMotionEnabled() || field.ActualHeight >= 100);
        bool NativeTextEditor() => field is TextBox or RichEditBox;
        pressFeedback.Tick += (_, _) => { pressFeedback.Stop(); Refresh(); };
        void StyleParts() {
            field.ApplyTemplate();
            AlignTextContent(field);
            if (field is KronoTextBox { SurfaceFeedbackEnabled: false }) return;
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
                surfaceColors = null;
                surfaceMotion = null;
                face.BorderBrush = rim;
                if (NativeTextEditor()) face.BorderThickness = field.BorderThickness;
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
                                setter.Target?.Path?.Path is "BorderElement.BorderBrush" or "Background.BorderBrush" or "BorderElement.Background" or "Background.Background" ||
                                NativeTextEditor() && setter.Target?.Path?.Path == "BorderElement.BorderThickness").ToArray())
                                state.Setters.Remove(setter);
                            if (state.Storyboard is { } storyboard)
                                foreach (var animation in storyboard.Children.Where(animation =>
                                    (Storyboard.GetTargetName(animation) is "BorderElement" or "Background") &&
                                    ((Storyboard.GetTargetProperty(animation) is "BorderBrush" or "Background") ||
                                     NativeTextEditor() && Storyboard.GetTargetProperty(animation) == "BorderThickness")).ToArray())
                                    storyboard.Children.Remove(animation);
                        }
                    }
                    if ((group.Name is "CommonStates" or "FocusStates") && group.Transitions.Count == 0)
                        group.Transitions.Add(new VisualTransition { GeneratedDuration = new Duration(TimeSpan.FromMilliseconds(180)) });
                }
            Refresh();
        }
        field.Loaded += (_, _) => StyleParts();
        if (field is NumberBox)
            field.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => {
                if (field.IsLoaded && field.Visibility == Visibility.Visible) StyleParts();
            });
        if (field.IsLoaded) StyleParts();

        void Refresh()
        {
            if (field is KronoTextBox { SurfaceFeedbackEnabled: false }) return;
            var focused = field.FocusState != FocusState.Unfocused ||
                field is KronoComboBox { IsEditable: true } && Descendants(field).OfType<TextBox>().Any(editor => editor.FocusState != FocusState.Unfocused);
            var active = field.IsEnabled && (focused || hover);
            var pressing = field.IsEnabled && (pressed || pressFeedback.IsEnabled);
            if (IsLargeEditor() || PressMotionEnabled() && field is not PasswordBox) {
                var motion = IsLargeEditor() && active ? (1.006f, -1.5f) :
                    (PressMotionEnabled() && !IsLargeEditor() && pressing ? .98f : 1f, 0f);
                if (surfaceMotion != motion) {
                    surfaceMotion = motion;
                    AnimateSurface(field, motion.Item1, companion, motion.Item2);
                }
            }
            if (rim is null || shade is null || fill is null) return;
            var theme = field.ActualTheme;
            var role = active ? (focused ? "KronoAccentForeground" : "KronoOutlineHover") : "KronoInputRim";
            var border = ((SolidColorBrush)KronoDesign.ThemeBrush(role, theme)).Color;
            var face = ((SolidColorBrush)KronoDesign.ThemeBrush("KronoInputFace", theme)).Color;
            var shadow = field.IsEnabled ? ((SolidColorBrush)KronoDesign.ThemeBrush("KronoInputShade", theme)).Color : face;
            var colors = (border, shadow, face);
            if (surfaceColors == colors) return;
            if (surfaceColors is null) {
                rim.Color = border;
                shade.Color = shadow;
                fill.Color = face;
                surfaceColors = colors;
                return;
            }
            surfaceColors = colors;
            var storyboard = new Storyboard();
            AddColorAnimation(storyboard, rim, border);
            AddColorAnimation(storyboard, shade, shadow);
            AddColorAnimation(storyboard, fill, face);
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
            if (!PressMotionEnabled()) return;
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
        field.Unloaded += (_, _) => {
            hover = pressed = false;
            pressFeedback.Stop();
            if (surfaceMotion is not null) AnimateSurface(field, 1, companion);
            surfaceMotion = null;
        };
    }

    internal static void StyleButton(ButtonBase button)
    {
        if (StyledButtons.TryGetValue(button, out _)) return;
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
        AttachFlatHover(button);
    }

    internal static void AttachFlatHover(ButtonBase button)
    {
        if (StyledButtons.TryGetValue(button, out _)) return;
        button.Template = (ControlTemplate)Application.Current.Resources["KronoFlatIconTemplate"];
        Border? tint = null;
        ContentPresenter? content = null;
        SolidColorBrush? paint = null;
        Storyboard? motion = null;
        bool over = false, down = false;
        void Update(bool immediate = false)
        {
            if (tint is null || paint is null) return;
            if (content is not null) content.Opacity = button.IsEnabled ? 1 : .45;
            var highContrast = new AccessibilitySettings().HighContrast;
            var role = down ? "ButtonBackgroundPressed" : "ButtonBackgroundPointerOver";
            var color = ((SolidColorBrush)KronoDesign.ThemeBrush(role, button.ActualTheme)).Color;
            var visible = button.IsEnabled && (over || down);
            if (highContrast) color = Microsoft.UI.Colors.Transparent;
            else if (!visible) color.A = 0;
            var from = paint.Color;
            motion?.Stop();
            paint.Color = color;
            if (immediate || highContrast || !new UISettings().AnimationsEnabled) return;
            var animation = new ColorAnimation {
                From = from, To = color, EnableDependentAnimation = true, Duration = new Duration(TimeSpan.FromMilliseconds(135)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, paint);
            Storyboard.SetTargetProperty(animation, "Color");
            motion = new Storyboard { Children = { animation } };
            motion.Begin();
        }
        void Reset() { over = down = false; Update(true); }
        StyledButtons.Add(button, Reset);
        void BindTemplate()
        {
            button.ApplyTemplate();
            tint = Descendants(button).OfType<Border>().FirstOrDefault(part => part.Name == "KronoFlatTint");
            content = Descendants(button).OfType<ContentPresenter>().FirstOrDefault(part => part.Name == "KronoFlatContent");
            if (tint is null) return;
            tint.Background = paint = new SolidColorBrush();
            Reset();
        }
        button.Loaded += (_, _) => BindTemplate();
        if (button.IsLoaded) BindTemplate();
        button.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => { over = true; Update(); }), true);
        button.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => { over = down = false; Update(); }), true);
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { down = true; Update(); }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => { down = false; Update(); }), true);
        button.PointerCaptureLost += (_, _) => { down = false; Update(); };
        button.PointerCanceled += (_, _) => Reset();
        button.IsEnabledChanged += (_, _) => Reset();
        button.ActualThemeChanged += (_, _) => Update(true);
        button.Unloaded += (_, _) => Reset();
    }

    static void AnimateSurface(Control field, float scale, FrameworkElement? companion, float lift = 0)
    {
        if (VisualTreeHelper.GetChildrenCount(field) == 0 || VisualTreeHelper.GetChild(field, 0) is not Grid root) return;
        // The native templates put the header in row 0, input in row 1, and description in row 2.
        var surfaces = root.Children.OfType<FrameworkElement>().Where(element => Grid.GetRow(element) == 1 && element is not Popup).ToArray();
        var face = surfaces.FirstOrDefault(element => element.Name is "BorderElement" or "Background" or "InputBox");
        if (face is null) return;
        // Native text hit testing and the caret must share untransformed editor coordinates.
        if (field is TextBox or RichEditBox) surfaces = [face];
        var center = face.ActualOffset + new System.Numerics.Vector3(face.ActualSize / 2, 0);
        var animationsEnabled = new UISettings().AnimationsEnabled;
        if (!animationsEnabled) { scale = 1; lift = 0; }
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
            foreach (var (property, value) in new[] { ("ScaleX", scale), ("ScaleY", scale), ("TranslateY", lift) }) {
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

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
