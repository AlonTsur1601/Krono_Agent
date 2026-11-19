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
        Set(dark, "KronoContextRail", halloween ? "281B34" : "1A2430");
        Set(dark, "KronoSurface", halloween ? "2B1D39" : "1B2532");
        Set(dark, "KronoInputFace", halloween ? "2B1D39" : "19232F");
        SetFace("KronoInputRim", halloween ? "160E20" : "0B1017", halloween ? "553764" : "344452");
        ((LinearGradientBrush)dark["KronoInputRim"]).GradientStops[2].Color = Parse(halloween ? "553764" : "344452");
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
    internal static void RefreshTypography(DependencyObject root)
    {
        var text = TextFont; var code = CodeFont; var textSize = TextSize; var codeSize = CodeSize;
        Refresh(root);
        void Refresh(DependencyObject element, bool codeContext = false)
        {
            if (element is IconElement) return;
            var isCode = codeContext;
            if (element is Control control) {
                if (control is NumberBox) KronoInputMotion.Attach(control);
                var id = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control);
                var gameCode = control.Tag?.ToString() == "game-code";
                isCode |= id is "TerminalInput" or "TerminalOutput" or "ToolApprovalDetails" || gameCode;
                control.FontFamily = isCode ? code : text;
                if (!gameCode && !id.StartsWith("GameCell", StringComparison.Ordinal) && control is TextBox or PasswordBox or ComboBox or NumberBox or Button or AppBarButton) control.FontSize = isCode ? codeSize : control.Tag?.ToString() == "caption" ? Math.Max(11, textSize - 3) : textSize;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Refresh(VisualTreeHelper.GetChild(element, i), isCode);
        }
    }
    public static void Present(ContentControl host, FrameworkElement content)
    {
        if (ReferenceEquals(host.Content, content)) { RefreshTypography(content); return; }
        host.Content = content;
        RefreshTypography(content);
        AnimateOpacity(host, 0);
    }

    public static void AnimateThemeChange(FrameworkElement surface) => AnimateOpacity(surface, .78f);

    internal static void StyleTabHeader(TabViewItem tab)
    {
        tab.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        var hovered = false;
        var pressed = false;
        void Refresh()
        {
            if (!tab.IsLoaded || new AccessibilitySettings().HighContrast) return;
            var surface = ThemeBrush(pressed ? "ButtonBackgroundPressed" : hovered ? "ButtonBackgroundPointerOver" :
                tab.IsSelected ? "KronoSurface" : "KronoSidebar", tab.ActualTheme);
            tab.Background = surface;
            tab.Resources["TabViewItemHeaderBackgroundSelected"] = surface;
            tab.Resources["TabViewItemHeaderBackgroundSelectedPointerOver"] = surface;
            tab.Resources["TabViewItemHeaderBackgroundSelectedPressed"] = surface;
            foreach (var part in KronoInputMotion.Descendants(tab).ToArray()) {
                if (part is Button { Name: "CloseButton" } close) {
                    KronoInputMotion.StyleButton(close);
                    close.VerticalAlignment = VerticalAlignment.Center;
                    close.HorizontalAlignment = HorizontalAlignment.Right;
                    close.Margin = new Thickness(3, 0, 0, 0);
                }
                if (part is Grid { Name: "TabContainer" } header) {
                    header.MinHeight = 36;
                    if (header.Children.OfType<ContentPresenter>().FirstOrDefault(child => child.Name == "ContentPresenter") is { } title) {
                        title.HorizontalAlignment = HorizontalAlignment.Center;
                        title.VerticalAlignment = VerticalAlignment.Center;
                    }
                    var face = header.Children.OfType<Border>().FirstOrDefault(child => child.Name == "KronoTabFace");
                    if (face is null) {
                        face = new Border { Name = "KronoTabFace", CornerRadius = new CornerRadius(9, 9, 0, 0),
                            IsHitTestVisible = false, Margin = new Thickness(-header.Padding.Left, -header.Padding.Top,
                                -header.Padding.Right, -header.Padding.Bottom) };
                        Grid.SetColumnSpan(face, header.ColumnDefinitions.Count);
                        header.Children.Insert(0, face);
                    }
                    face.Background = surface;
                }
                if (part is Microsoft.UI.Xaml.Shapes.Path { Name: "SelectedBackgroundPath" or "LeftRadiusRenderArc" or "RightRadiusRenderArc" } decoration)
                    decoration.Opacity = 0;
            }
        }
        tab.PointerEntered += (_, _) => { hovered = true; Refresh(); };
        tab.PointerExited += (_, _) => { hovered = pressed = false; Refresh(); };
        tab.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => { pressed = true; Refresh(); }), true);
        tab.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => { pressed = false; Refresh(); }), true);
        tab.PointerCaptureLost += (_, _) => { pressed = false; Refresh(); };
        KronoInputMotion.RegisterPaletteRefresh(tab, Refresh);
        tab.Loaded += (_, _) => Refresh();
        tab.ActualThemeChanged += (_, _) => Refresh();
        tab.RegisterPropertyChangedCallback(TabViewItem.IsSelectedProperty, (_, _) => tab.DispatcherQueue.TryEnqueue(Refresh));
    }

    internal static Grid ScrollPage(FrameworkElement content, string background = "KronoBackground")
    {
        var root = new Grid();
        var scroll = content as ScrollViewer ?? new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
        scroll.LayoutUpdated += (_, _) => RefreshEdges();
        RefreshEdges();
        return root;

        static LinearGradientBrush Fade(Windows.UI.Color start, Windows.UI.Color end) => new() {
            StartPoint = new(0, 0), EndPoint = new(0, 1),
            GradientStops = { new GradientStop { Offset = 0, Color = start }, new GradientStop { Offset = 1, Color = end } }
        };
    }

    internal static void StyleTabs(TabView tabs)
    {
        tabs.TabStripHeader = new Border { Width = 9 };
        tabs.TabWidthMode = TabViewWidthMode.SizeToContent;
        tabs.CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.Always;
        tabs.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        tabs.Loaded += (_, _) => {
            tabs.ApplyTemplate();
            if (KronoInputMotion.Descendants(tabs).OfType<Button>().FirstOrDefault(button => button.Name == "AddButton") is not { } add) return;
            add.Style = (Style)Application.Current.Resources["KronoButton"];
            add.MinHeight = 0;
            add.Width = 30;
            add.Height = 27;
            add.Padding = new Thickness(0);
            add.CornerRadius = new CornerRadius(7);
            add.Content = new SymbolIcon(Symbol.Add);
            add.FontSize = TextSize;
            add.VerticalAlignment = VerticalAlignment.Center;
            if (add.Parent is Border container) {
                container.VerticalAlignment = VerticalAlignment.Bottom;
                container.Height = 36;
                container.Padding = new Thickness(3, 0, 0, 0);
            }
        };
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

    internal static Expander Section(string title, FrameworkElement content)
    {
        var chevron = new FontIcon { Glyph = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(chevron, 1);
        header.Children.Add(chevron);
        var expander = new Expander { Header = header, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expander, title);
        expander.Expanding += (_, _) => chevron.Glyph = "\uE70E";
        expander.Collapsed += (_, _) => chevron.Glyph = "\uE70D";
        expander.Loaded += (_, _) => {
            if (KronoInputMotion.Descendants(expander).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>()
                .FirstOrDefault(button => button.Name == "ExpanderHeader") is not { } toggle) return;
            toggle.Style = (Style)Application.Current.Resources["KronoToggleButton"];
            toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            toggle.CornerRadius = new CornerRadius(10);
            toggle.Padding = new Thickness(12, 6, 12, 6);
            void Palette()
            {
                toggle.Background = ThemeBrush("KronoButtonFace", toggle.ActualTheme);
                toggle.BorderBrush = ThemeBrush("KronoBorder", toggle.ActualTheme);
            }
            Palette();
            toggle.ActualThemeChanged += (_, _) => Palette();
            KronoInputMotion.RegisterPaletteRefresh(toggle, Palette);
        };
        return expander;
    }

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
