using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

// Shared visual roles for every native page; gameplay animation is separate.
public static class KronoDesign
{
    internal enum OutlineGlyph { Search, Folder, Pin, Unpin, Archive, Restore, Star, Clock }

    internal static FrameworkElement OutlineIcon(OutlineGlyph glyph, Brush? stroke = null, bool filled = false)
    {
        var data = glyph switch {
            OutlineGlyph.Search => "M13.5,8.5 A5,5 0 1 0 3.5,8.5 A5,5 0 1 0 13.5,8.5 M12.1,12.1 L16.5,16.5",
            OutlineGlyph.Folder => "M4.5,4.75 H8 L10,6.75 H15.5 Q17.25,6.75 17.25,8.5 V13.5 Q17.25,15.25 15.5,15.25 H4.5 Q2.75,15.25 2.75,13.5 V6.5 Q2.75,4.75 4.5,4.75 Z",
            OutlineGlyph.Pin => "M10.55,15.85 L4.33,9.52 L8.37,7.91 Q8.76,7.76 8.93,7.39 L10.52,3.77 Q10.83,3.1 11.36,3.1 Q11.76,3.1 12.03,3.39 L16.58,7.95 Q17.17,8.55 16.78,9.2 Q16.61,9.48 16.25,9.63 L12.38,11.19 Q11.91,11.38 11.76,11.89 Z M7.45,12.67 L3.4,16.72",
            OutlineGlyph.Unpin => "M8.93,7.39 L10.52,3.77 Q10.83,3.1 11.36,3.1 Q11.76,3.1 12.03,3.39 L16.58,7.95 Q17.17,8.55 16.78,9.2 Q16.61,9.48 16.25,9.63 L12.91,11 M7.98,8.07 L4.33,9.52 L10.55,15.85 L11.67,12.11 M7.45,12.67 L3.4,16.72 M3.1,3.1 L16.9,16.9",
            OutlineGlyph.Archive => "M3,4.5 H17 V8 H3 Z M4.5,8 V14.25 Q4.5,15.5 5.75,15.5 H14.25 Q15.5,15.5 15.5,14.25 V8 M8,10.75 H12",
            OutlineGlyph.Restore => "M7.75,5 L4.25,8.5 L7.75,12 M4.25,8.5 H12.5 A3.25,3.25 0 0 1 12.5,15 H9",
            OutlineGlyph.Star => "M10,2.75 L12.2,7.2 L17.1,7.9 L13.55,11.35 L14.4,16.25 L10,13.95 L5.6,16.25 L6.45,11.35 L2.9,7.9 L7.8,7.2 Z",
            OutlineGlyph.Clock => "M18,10 A8,8 0 1 0 2,10 A8,8 0 1 0 18,10 Z M10,5.5 V10 H14",
            _ => throw new ArgumentOutOfRangeException(nameof(glyph))
        };
        // The fixed canvas keeps a 1.5-DIP outline and centers the ink without clipping.
        var icon = (FrameworkElement)Microsoft.UI.Xaml.Markup.XamlReader.Load($$"""
            <Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="20" Height="20" IsHitTestVisible="False">
                <Path Data="{{data}}" Stroke="{ThemeResource TextFillColorPrimaryBrush}" StrokeThickness="1.5"
                      StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
            </Canvas>
            """);
        if (icon is Canvas canvas && canvas.Children[0] is Microsoft.UI.Xaml.Shapes.Path path) {
            if (stroke is not null) path.Stroke = stroke;
            if (filled) path.Fill = path.Stroke;
        }
        return icon;
    }

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
        Set(dark, "KronoContextRail", halloween ? "281B34" : "1A2430");
        Set(dark, "KronoSurface", halloween ? "2B1D39" : "1B2532");
        Set(dark, "KronoInputFace", halloween ? "2B1D39" : "19232F");
        Set(dark, "KronoInputRim", halloween ? "553764" : "344452");
        Set(dark, "KronoInputShade", halloween ? "20152D" : "111A24");
        Set(dark, "KronoDisabledFace", halloween ? "463952" : "354353");
        Set(dark, "KronoDisabledBase", halloween ? "2E2439" : "26323E");
        Set(dark, "KronoDisabledInk", halloween ? "E4D8EC" : "D2DCE5");
        SetFace("KronoButtonFace", halloween ? "452F56" : "2D3A49", halloween ? "271A35" : "1A2430");
        SetFace("KronoPrimaryFace", halloween ? "FFCC96" : "A4F0C8", halloween ? "F6934F" : "69D6A0");
        Set(dark, "KronoBorder", halloween ? "442D56" : "2B3B4C");
        Set(dark, "KronoLoginBackdrop", halloween ? "1D1329" : "111D20");
        Set(dark, "KronoOutlineHover", halloween ? "A077A6" : "537D70");
        Set(dark, "ButtonBackgroundPointerOver", halloween ? "3B284B" : "293D48");
        Set(dark, "ButtonBackgroundPressed", halloween ? "503460" : "344D59");
        Set(dark, "KronoMuted", halloween ? "CEAEDF" : "9AABBF");
        Set(dark, "KronoAccentForeground", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "KronoAccentFill", halloween ? "FFAD62" : "84E6B1");
        Set(dark, "KronoPrimaryBase", halloween ? "B35B2D" : "3B9F72");
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
        void SetFace(string key, string top, string bottom)
        {
            var stops = ((LinearGradientBrush)dark[key]).GradientStops;
            stops[0].Color = Parse(top);
            stops[1].Color = Parse(bottom);
        }
        static Windows.UI.Color Parse(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
    public static Brush ThemeBrush(string key, ElementTheme theme)
    {
        var dictionaries = Application.Current.Resources.ThemeDictionaries;
        return (Brush)((ResourceDictionary)dictionaries[theme == ElementTheme.Light ? "Light" : "Default"])[key];
    }
    public static void ApplyControlAccents(FrameworkElement root)
    {
        var resources = root.Resources;
        var ink = ThemeBrush("KronoAccentForeground", root.ActualTheme);
        var fill = ThemeBrush("KronoAccentFill", ElementTheme.Dark);
        var hover = ThemeBrush("KronoAccentHover", ElementTheme.Dark);
        var pressed = ThemeBrush("KronoAccentPressed", ElementTheme.Dark);
        var onAccent = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var surface = ThemeBrush("KronoInputFace", root.ActualTheme);
        var outlineHover = ThemeBrush("KronoOutlineHover", root.ActualTheme);
        resources["TextControlThemeMinHeight"] = Application.Current.Resources["KronoControlMinHeight"];
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1, 4, 1, 1);
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
        foreach (var key in new[] { "TextControlSelectionHighlightColor", "AccentButtonBackground", "ToggleButtonBackgroundChecked" })
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

        resources["CheckBoxCheckBackgroundFillUnchecked"] = ThemeBrush("KronoButtonFace", root.ActualTheme);
        resources["CheckBoxCheckBackgroundStrokeUnchecked"] = ThemeBrush("KronoBorder", root.ActualTheme);
        resources["CheckBoxCheckBackgroundFillUncheckedPointerOver"] = ThemeBrush("ButtonBackgroundPointerOver", root.ActualTheme);
        resources["CheckBoxCheckBackgroundFillUncheckedPressed"] = ThemeBrush("ButtonBackgroundPressed", root.ActualTheme);
        resources["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = outlineHover;
        resources["CheckBoxCheckBackgroundStrokeUncheckedPressed"] = outlineHover;
        resources["CheckBoxCheckBackgroundFillUncheckedDisabled"] = ThemeBrush("KronoDisabledFace", root.ActualTheme);
        resources["CheckBoxCheckBackgroundStrokeUncheckedDisabled"] = ThemeBrush("KronoDisabledBase", root.ActualTheme);
        resources["CheckBoxCheckBackgroundFillChecked"] = ThemeBrush("KronoPrimaryFace", root.ActualTheme);
        resources["CheckBoxCheckBackgroundStrokeChecked"] = ThemeBrush("KronoPrimaryBase", root.ActualTheme);
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
        Application.Current.Resources["KronoDisplayFont"] = text;
        Application.Current.Resources["KronoCodeFont"] = code;
        Application.Current.Resources["KronoTextSize"] = textSize;
        Application.Current.Resources["KronoCodeSize"] = codeSize;
        RefreshTypography(MainWindow.Instance?.Content is DependencyObject windowRoot ? windowRoot : root);
    }
    internal static void RefreshTypography(DependencyObject root)
    {
        var text = TextFont; var code = CodeFont; var textSize = TextSize; var codeSize = CodeSize;
        Refresh(root);
        void Refresh(DependencyObject element, bool codeContext = false)
        {
            if (element is IconElement) return;
            var codeLabel = element is FrameworkElement { Tag: "game-code" or "code" };
            var isCode = codeContext || codeLabel;
            if (element is Control control) {
                var id = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control);
                isCode |= id is "TerminalInput" or "TerminalOutput" or "ToolApprovalDetails";
                control.FontFamily = isCode ? code : text;
                if (!codeLabel && !id.StartsWith("GameCell", StringComparison.Ordinal) && control is TextBox or PasswordBox or ComboBox or NumberBox or Button or AppBarButton) control.FontSize = isCode ? codeSize : control.Tag?.ToString() == "caption" ? Math.Max(11, textSize - 3) : textSize;
            }
            if (element is TextBlock label) label.FontFamily = isCode ? code : text;
            if (element is RichTextBlock rich) rich.FontFamily = isCode ? code : text;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Refresh(VisualTreeHelper.GetChild(element, i), isCode);
        }
    }
    public static void Present(ContentControl host, FrameworkElement content)
    {
        if (ReferenceEquals(host.Content, content)) { RefreshTypography(content); return; }
        host.Content = content;
        RefreshTypography(content);
        AnimateOpacity(host, 0);
        if (new UISettings().AnimationsEnabled) {
            ElementCompositionPreview.SetIsTranslationEnabled(host, true);
            var visual = ElementCompositionPreview.GetElementVisual(host);
            var rise = visual.Compositor.CreateVector3KeyFrameAnimation();
            rise.Duration = PageMotion;
            rise.InsertKeyFrame(0, new(0, 9, 0));
            rise.InsertKeyFrame(1, System.Numerics.Vector3.Zero, visual.Compositor.CreateCubicBezierEasingFunction(new(.18f, .8f), new(.3f, 1)));
            visual.StartAnimation("Translation", rise);
        }
    }

    internal static MenuFlyout CreateMenu()
    {
        var menu = new MenuFlyout {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["KronoMenuPresenter"]
        };
        menu.Closed += (_, _) => ResetItems(menu.Items);
        return menu;

        static void ResetItems(IEnumerable<MenuFlyoutItemBase> items)
        {
            foreach (var item in items) {
                VisualStateManager.GoToState(item, "Normal", false);
                if (item is MenuFlyoutSubItem submenu) ResetItems(submenu.Items);
            }
        }
    }

    internal static void AnimateMenuOpening(FrameworkElement surface)
    {
        if (!new UISettings().AnimationsEnabled) return;
        AnimateOpacity(surface, .35f);
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, new(0, -5, 0));
        animation.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        animation.Duration = TimeSpan.FromMilliseconds(135);
        visual.StartAnimation("Translation", animation);
    }

    internal static Task BeginThemeChange(FrameworkElement surface)
    {
        if (!new UISettings().AnimationsEnabled) return Task.CompletedTask;
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        fade.InsertKeyFrame(1, .72f);
        fade.Duration = TimeSpan.FromMilliseconds(75);
        visual.StartAnimation("Opacity", fade);
        return Task.Delay(fade.Duration);
    }

    internal static void FinishThemeChange(FrameworkElement surface)
    {
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        if (!new UISettings().AnimationsEnabled) {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
            return;
        }
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(120);
        visual.StartAnimation("Opacity", fade);
    }

    const double TabHeight = 34;
    const double TabButtonSize = 26;
    const double TabStripInset = 6;
    const double TabWidth = 164;

    internal static void StyleTabHeader(TabViewItem tab, string selectedSurface = "KronoSurface")
    {
        tab.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        tab.MinWidth = 12;
        tab.MaxWidth = TabWidth;
        tab.HeaderTemplate = (DataTemplate)Application.Current.Resources["KronoTabTitle"];
        tab.MinHeight = TabHeight;
        tab.Padding = new Thickness(0);
        tab.Resources["TabViewSelectedItemHeaderMargin"] = new Thickness(0);
        tab.Resources["TabViewSelectedItemBorderThickness"] = new Thickness(0);
        var hovered = false;
        var pressed = false;
        Border? face = null;
        var firstLoad = true;
        void Refresh()
        {
            if (face is null || new AccessibilitySettings().HighContrast) return;
            face.Background = ThemeBrush(pressed ? "ButtonBackgroundPressed" :
                hovered ? "ButtonBackgroundPointerOver" : tab.IsSelected ? selectedSurface : "KronoSidebar", tab.ActualTheme);
        }
        tab.Loaded += (_, _) => {
            var highContrast = new AccessibilitySettings().HighContrast;
            if (!highContrast) tab.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (var part in KronoInputMotion.Descendants(tab).ToArray()) {
                if (part is Button { Name: "CloseButton" } close) {
                    StyleTabButton(close, add: false);
                    close.Margin = new Thickness(4, 0, 0, 0);
                    close.HorizontalAlignment = HorizontalAlignment.Right;
                }
                if (!highContrast && part is Grid { Name: "TabContainer" } header) {
                    header.MinHeight = TabHeight;
                    header.Margin = new Thickness(0);
                    header.BorderThickness = new Thickness(0);
                    header.VerticalAlignment = VerticalAlignment.Stretch;
                    if (header.Children.OfType<ContentPresenter>().FirstOrDefault(child => child.Name == "ContentPresenter") is { } title) {
                        title.HorizontalAlignment = HorizontalAlignment.Stretch;
                        title.VerticalAlignment = VerticalAlignment.Center;
                    }
                    face = header.Children.OfType<Border>().FirstOrDefault(child => child.Name == "KronoTabFace");
                    if (face is null) {
                        face = new Border { Name = "KronoTabFace", CornerRadius = new CornerRadius(9, 9, 0, 0),
                            IsHitTestVisible = false, Margin = new Thickness(-header.Padding.Left, -header.Padding.Top,
                                -header.Padding.Right, -header.Padding.Bottom),
                            BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) } };
                        Grid.SetColumnSpan(face, header.ColumnDefinitions.Count);
                        header.Children.Insert(0, face);
                    }
                }
                if (!highContrast && part is Microsoft.UI.Xaml.Shapes.Path { Name: "SelectedBackgroundPath" or "LeftRadiusRenderArc" or "RightRadiusRenderArc" } decoration)
                    decoration.Opacity = 0;
            }
            Refresh();
            if (firstLoad && new UISettings().AnimationsEnabled)
                AnimateTabOffset(tab, -12, 0, TimeSpan.FromMilliseconds(150));
            firstLoad = false;
        };
        tab.PointerEntered += (_, _) => { hovered = true; Refresh(); };
        tab.PointerExited += (_, _) => { hovered = pressed = false; Refresh(); };
        tab.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => { pressed = true; Refresh(); }), true);
        tab.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => { pressed = false; Refresh(); }), true);
        tab.PointerCaptureLost += (_, _) => { pressed = false; Refresh(); };
        KronoInputMotion.RegisterPaletteRefresh(tab, Refresh);
        tab.ActualThemeChanged += (_, _) => Refresh();
        tab.RegisterPropertyChangedCallback(TabViewItem.IsSelectedProperty, (_, _) => Refresh());
    }

    internal static async Task FadeTabClose(TabViewItem tab)
    {
        if (!tab.IsLoaded || !new UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(tab);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(115);
        visual.StartAnimation("Opacity", fade);
        AnimateTabOffset(tab, null, -8, fade.Duration);
        await Task.Delay(fade.Duration);
    }

    static void AnimateTabOffset(TabViewItem tab, float? from, float to, TimeSpan duration)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(tab, true);
        var visual = ElementCompositionPreview.GetElementVisual(tab);
        var motion = visual.Compositor.CreateVector3KeyFrameAnimation();
        if (from is { } offset) motion.InsertKeyFrame(0, new(offset, 0, 0));
        else motion.InsertExpressionKeyFrame(0, "this.StartingValue");
        motion.InsertKeyFrame(1, new(to, 0, 0));
        motion.Duration = duration;
        visual.StartAnimation("Translation", motion);
    }

    internal static void MarkDestructive(Control control)
    {
        void Refresh() => control.Foreground = ThemeBrush("KronoDangerForeground", control.ActualTheme);
        Refresh();
        control.Loaded += (_, _) => Refresh();
        control.ActualThemeChanged += (_, _) => Refresh();
        KronoInputMotion.RegisterPaletteRefresh(control, Refresh);
    }

    internal static Grid ScrollPage(FrameworkElement content, string background = "KronoBackground")
    {
        var root = new Grid();
        var scroll = content as ScrollViewer ?? new ScrollViewer { Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var top = new Border { Height = 14, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        var bottom = new Border { Height = 14, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        root.Children.Add(top);
        root.Children.Add(bottom);
        void RefreshPalette()
        {
            var fill = ((SolidColorBrush)ThemeBrush(background, root.ActualTheme)).Color;
            var clear = fill;
            clear.A = 0;
            top.Background = Fade(fill, clear);
            bottom.Background = Fade(clear, fill);
        }
        void RefreshEdges()
        {
            top.Visibility = scroll.VerticalOffset > 1 ? Visibility.Visible : Visibility.Collapsed;
            bottom.Visibility = scroll.VerticalOffset + 1 < scroll.ScrollableHeight ? Visibility.Visible : Visibility.Collapsed;
        }
        root.Loaded += (_, _) => { RefreshPalette(); RefreshEdges(); };
        root.ActualThemeChanged += (_, _) => RefreshPalette();
        KronoInputMotion.RegisterPaletteRefresh(root, RefreshPalette);
        scroll.ViewChanged += (_, _) => RefreshEdges();
        scroll.SizeChanged += (_, _) => RefreshEdges();
        content.SizeChanged += (_, _) => RefreshEdges();
        RefreshEdges();
        return root;

        static LinearGradientBrush Fade(Windows.UI.Color start, Windows.UI.Color end) => new() {
            StartPoint = new(0, 0), EndPoint = new(0, 1),
            GradientStops = { new GradientStop { Offset = 0, Color = start }, new GradientStop { Offset = 1, Color = end } }
        };
    }

    internal static Grid CreateTabStrip(TabView tabs, Action addTab, FrameworkElement? commands = null)
    {
        tabs.IsAddTabButtonVisible = true;
        tabs.TabStripHeader = new Border { Width = 0 };
        // Compensate for the native TabView's leading inset.
        tabs.Margin = new Thickness(-2.5, 0, 0, 0);
        tabs.Padding = new Thickness(0, TabStripInset, 0, 0);
        tabs.BorderThickness = new Thickness(0);
        tabs.TabWidthMode = TabViewWidthMode.Equal;
        tabs.Resources["TabViewItemMinWidth"] = 12.0;
        tabs.Resources["TabViewItemMaxWidth"] = TabWidth;
        tabs.CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.OnPointerOver;
        tabs.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        tabs.AddTabButtonClick += (_, _) => addTab();
        void RevealSelection()
        {
            if (tabs.SelectedItem is TabViewItem selected)
                selected.DispatcherQueue.TryEnqueue(() => selected.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }));
        }
        tabs.SelectionChanged += (_, _) => RevealSelection();
        var footer = new StackPanel {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Height = TabHeight,
            Margin = new Thickness(0),
            Padding = new Thickness(4, 0, TabStripInset, 0), Spacing = 6
        };
        if (commands is not null) footer.Children.Add(commands);
        var header = new Grid { Height = TabHeight + TabStripInset };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Clip the tab backgrounds at the shared edge without reserving a separator row.
        var viewport = new Grid();
        var clip = new RectangleGeometry();
        viewport.Clip = clip;
        viewport.SizeChanged += (_, args) => {
            clip.Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
            RevealSelection();
        };
        viewport.Children.Add(tabs);
        header.Children.Add(viewport);
        header.Children.Add(footer);
        Grid.SetColumn(footer, 1);

        tabs.Loaded += (_, _) => {
            tabs.ApplyTemplate();
            var strip = KronoInputMotion.Descendants(tabs).OfType<Grid>().FirstOrDefault(part => part.Name == "TabContainerGrid");
            if (strip is null) return;
            // The native list's group transition batches inserts made within the same layout pass.
            // Use our per-header motion so each of those inserts has its own animation.
            foreach (var list in KronoInputMotion.Descendants(strip).OfType<ListViewBase>())
                list.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            if (KronoInputMotion.Descendants(strip).OfType<Button>().FirstOrDefault(part => part.Name == "AddButton") is { } add) {
                StyleTabButton(add, add: true);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(add, "WorkspaceAddTab");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, "Add new tab");
                ToolTipService.SetToolTip(add, "Add new tab");
                if (add.Parent is Border addCell) {
                    addCell.Height = TabHeight;
                    addCell.Padding = new Thickness(3, 0, 3, 0);
                }
            }
            if (KronoInputMotion.Descendants(strip).OfType<ScrollContentPresenter>()
                .FirstOrDefault(part => part.Name == "ScrollContentPresenter") is { } presenter)
                presenter.Padding = new Thickness(0);
            foreach (var line in KronoInputMotion.Descendants(strip).OfType<Border>()
                .Where(part => part.Name is "LeftBottomBorderLine" or "RightBottomBorderLine"))
                line.Opacity = 0;
            foreach (var button in KronoInputMotion.Descendants(strip).OfType<Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>()
                .Where(part => part.Name is "ScrollDecreaseButton" or "ScrollIncreaseButton")) {
                button.IsTabStop = false;
                button.IsHitTestVisible = false;
                button.MinWidth = button.Width = 0;
                if (button.Parent is Border container) container.MinWidth = container.Width = 0;
            }
        };
        return header;
    }

    static void StyleTabButton(Button button, bool add)
    {
        KronoInputMotion.StyleButton(button);
        button.Width = button.Height = TabButtonSize;
        button.CornerRadius = new CornerRadius(7);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        if (button.Content is not PathIcon) button.Content = TabButtonIcon(add);
    }

    internal static PathIcon NavigationIcon(Symbol symbol)
    {
        var data = symbol switch {
            Symbol.World => "F0 M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 Z M10,3.85 A6.15,6.15 0 1 1 10,16.15 A6.15,6.15 0 1 1 10,3.85 Z M9.075,3.85 H10.925 V9.075 H16.15 V10.925 H10.925 V16.15 H9.075 V10.925 H3.85 V9.075 H9.075 Z",
            Symbol.Download => "M9.25,2 H10.75 V10.2 L13.7,7.25 L14.75,8.3 L10,13.05 L5.25,8.3 L6.3,7.25 L9.25,10.2 Z M2,12 H3.5 V16.5 H16.5 V12 H18 V18 H2 Z",
            _ => throw new ArgumentOutOfRangeException(nameof(symbol))
        };
        return (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20' Data='{data}' />");
    }

    static PathIcon TabButtonIcon(bool add)
    {
        // Both symbols have symmetric ink bounds in the same 12-DIP box.
        var shape = add
            ? "M 5.25,0 L 6.75,0 6.75,5.25 12,5.25 12,6.75 6.75,6.75 6.75,12 5.25,12 5.25,6.75 0,6.75 0,5.25 5.25,5.25 Z"
            : "M 1,0 L 6,5 11,0 12,1 7,6 12,11 11,12 6,7 1,12 0,11 5,6 0,1 Z";
        var icon = (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{shape}' />");
        icon.Width = icon.Height = 12;
        return icon;
    }

    internal static Border FrameTabContent(UIElement content, string surface = "KronoSurface")
    {
        const float radius = 12;
        var frame = new Border { CornerRadius = new CornerRadius(radius), Child = content };
        void ApplyPalette() => frame.Background = ThemeBrush(surface, frame.ActualTheme);
        frame.Loaded += (_, _) => ApplyPalette();
        frame.ActualThemeChanged += (_, _) => ApplyPalette();
        var visual = ElementCompositionPreview.GetElementVisual(frame);
        var shape = visual.Compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new System.Numerics.Vector2(radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
        frame.SizeChanged += (_, args) => shape.Size = new System.Numerics.Vector2((float)args.NewSize.Width, (float)args.NewSize.Height);
        return frame;
    }

    static void AnimateOpacity(FrameworkElement surface, float from)
    {
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        if (!new UISettings().AnimationsEnabled) {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
            return;
        }

        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, from);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = PageMotion;
        visual.StartAnimation("Opacity", fade);
    }

    internal static FrameworkElement DiscreteSlider(string name, string[] labels, int selected, Action<int> change)
    {
        var title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var slider = new Slider { Minimum = 0, Maximum = labels.Length - 1, Value = selected, StepFrequency = 1,
            TickFrequency = 1, TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.BottomRight,
            IsThumbToolTipEnabled = false, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, name);
        void Palette()
        {
            slider.Resources["SliderTrackFill"] = ThemeBrush("KronoInputFace", slider.ActualTheme);
            foreach (var (suffix, color) in new[] { ("", "KronoAccentFill"), ("PointerOver", "KronoAccentHover"), ("Pressed", "KronoAccentPressed") }) {
                var brush = ThemeBrush(color, slider.ActualTheme);
                slider.Resources["SliderTrackValueFill" + suffix] = brush;
                slider.Resources["SliderThumbBackground" + suffix] = brush;
            }
            slider.Resources["SliderOuterThumbBackground"] = ThemeBrush("KronoSurface", slider.ActualTheme);
            slider.Resources["SliderThumbBorderBrush"] = ThemeBrush("KronoBorder", slider.ActualTheme);
        }
        void Refresh(int index) => title.Text = name + " · " + labels[index];
        Palette();
        slider.ActualThemeChanged += (_, _) => Palette();
        Refresh(selected);
        slider.ValueChanged += (_, args) => { var index = Math.Clamp((int)Math.Round(args.NewValue), 0, labels.Length - 1); Refresh(index); change(index); };
        var track = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 0, 12, 2),
            BorderThickness = new Thickness(1, 1, 1, 3), Child = slider };
        void PaintTrack() { track.Background = ThemeBrush("KronoButtonFace", track.ActualTheme); track.BorderBrush = ThemeBrush("KronoBorder", track.ActualTheme); }
        PaintTrack();
        track.ActualThemeChanged += (_, _) => PaintTrack();
        KronoInputMotion.RegisterPaletteRefresh(track, () => { Palette(); PaintTrack(); });
        return new StackPanel { Spacing = 5, Children = { title, track } };
    }

    internal static KronoSection Section(string title, FrameworkElement content) => new(title, content);

    internal static ToggleSwitch Toggle(string header, bool isOn)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = isOn, OnContent = "On", OffContent = "Off" };
        void Refresh()
        {
            toggle.FontFamily = TextFont;
            toggle.FontSize = TextSize;
            if (new AccessibilitySettings().HighContrast) return;
            var theme = toggle.ActualTheme;
            Brush Role(string key) => ThemeBrush(key, theme);
            var resources = toggle.Resources;
            resources["ToggleSwitchFillOff"] = Role("KronoInputFace");
            resources["ToggleSwitchStrokeOff"] = Role("KronoBorder");
            resources["ToggleSwitchFillOffPointerOver"] = ((SolidColorBrush)Role("ButtonBackgroundPointerOver")).Color;
            resources["ToggleSwitchFillOffPressed"] = ((SolidColorBrush)Role("ButtonBackgroundPressed")).Color;
            resources["ToggleSwitchStrokeOffPointerOver"] = ((SolidColorBrush)Role("KronoOutlineHover")).Color;
            resources["ToggleSwitchFillOn"] = Role("KronoPrimaryFace");
            resources["ToggleSwitchStrokeOn"] = Role("KronoPrimaryBase");
            resources["ToggleSwitchFillOnPointerOver"] = Role("KronoAccentHover");
            resources["ToggleSwitchFillOnPressed"] = Role("KronoAccentPressed");
            foreach (var state in new[] { "", "PointerOver", "Pressed" })
                resources["ToggleSwitchKnobFillOff" + state] = Role("KronoMuted");
        }
        Refresh();
        toggle.ActualThemeChanged += (_, _) => Refresh();
        toggle.Loaded += (_, _) => {
            Refresh();
            var trackParts = KronoInputMotion.Descendants(toggle).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>()
                .Where(shape => shape.Name is "OuterBorder" or "SwitchKnobBounds").ToArray();
            foreach (var track in trackParts) track.RadiusX = track.RadiusY = 7;
            if (KronoInputMotion.Descendants(toggle).OfType<Grid>().FirstOrDefault(grid => grid.Name == "SwitchKnob") is { } knob &&
                !KronoInputMotion.Descendants(toggle).OfType<Border>().Any(part => part.Name == "KronoToggleBase"))
                KronoPressSurface.AttachPulse(toggle, knob, 2);
            if (trackParts.FirstOrDefault() is { Parent: Grid host } trackFace &&
                !host.Children.OfType<Border>().Any(part => part.Name == "KronoToggleBase")) {
                var shadow = new Border { Name = "KronoToggleBase", CornerRadius = new CornerRadius(7), IsHitTestVisible = false,
                    Width = 40, Height = 20, Translation = new(0, 3, 0) };
                void PaintBase() => shadow.Background = ThemeBrush(toggle.IsOn ? "KronoPrimaryBase" : "KronoBorder", toggle.ActualTheme);
                PaintBase();
                toggle.Toggled += (_, _) => PaintBase();
                toggle.ActualThemeChanged += (_, _) => PaintBase();
                KronoInputMotion.RegisterPaletteRefresh(toggle, PaintBase);
                Grid.SetRow(shadow, Grid.GetRow(trackFace));
                Grid.SetColumn(shadow, Grid.GetColumn(trackFace));
                host.Children.Insert(host.Children.IndexOf(trackFace), shadow);
            }
        };
        KronoInputMotion.RegisterPaletteRefresh(toggle, Refresh);
        return toggle;
    }

    public static void NameTemplateButton(Control control, string part, string label)
    {
        if (KronoInputMotion.Descendants(control).OfType<Button>().FirstOrDefault(button => button.Name == part) is not { } target) return;
        if (part == "MoreButton") {
            target.Style = (Style)Application.Current.Resources["KronoButton"];
            target.MinHeight = 0;
            target.Width = target.Height = 36;
            target.Padding = new Thickness(0);
            target.CornerRadius = new CornerRadius(8);
            target.Content = new SymbolIcon(Symbol.More);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target, label);
        ToolTipService.SetToolTip(target, label);
    }
}
