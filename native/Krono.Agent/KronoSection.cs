using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

// Animate the measured body height so surrounding content follows the expansion.
internal sealed class KronoSection : Grid
{
    const double BodyInset = 16;
    readonly FrameworkElement content;
    readonly Border body;
    readonly ToggleButton headerButton;
    readonly FontIcon chevron;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    bool expanded;
    long start;
    double from, to;

    internal event EventHandler? Expanding;
    public bool IsExpanded {
        get => expanded;
        set {
            if (expanded == value) return;
            var previous = State;
            expanded = value;
            headerButton.IsChecked = value;
            chevron.Glyph = value ? "\uE70E" : "\uE70D";
            if (value) Expanding?.Invoke(this, EventArgs.Empty);
            Animate();
            FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous, State);
        }
    }

    internal KronoSection(string title, FrameworkElement content)
    {
        this.content = content;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chevron = new FontIcon { Glyph = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        SetColumn(chevron, 1);
        header.Children.Add(chevron);
        headerButton = new ToggleButton {
            Content = header, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(10)
        };
        AutomationProperties.SetName(headerButton, title);
        AutomationProperties.SetName(this, title);
        headerButton.Click += (_, _) => IsExpanded = headerButton.IsChecked == true;
        Children.Add(headerButton);
        var clip = new RectangleGeometry();
        body = new Border {
            Visibility = Visibility.Collapsed, Child = content, CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), Padding = new Thickness(BodyInset - 1), Clip = clip
        };
        body.SizeChanged += (_, args) => clip.Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        SetRow(body, 1);
        Children.Add(body);
        void Palette() {
            body.Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
            body.BorderBrush = KronoDesign.ThemeBrush("KronoBorder", ActualTheme);
        }
        Loaded += (_, _) => { Palette(); Finish(); };
        ActualThemeChanged += (_, _) => Palette();
        KronoInputMotion.RegisterPaletteRefresh(this, Palette);
        Unloaded += (_, _) => timer.Stop();
        timer.Tick += (_, _) => {
            var progress = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / 150;
            if (progress >= 1) { Finish(); return; }
            var eased = expanded ? 1 - Math.Pow(1 - progress, 3) : progress * progress;
            body.MaxHeight = Math.Max(0, from + (to - from) * eased);
        };
    }

    void Animate()
    {
        if (!IsLoaded || ActualWidth <= 0 || !new UISettings().AnimationsEnabled) { Finish(); return; }
        from = body.Visibility == Visibility.Visible ? body.ActualHeight : 0;
        body.Visibility = Visibility.Visible;
        if (expanded) content.Measure(new Windows.Foundation.Size(Math.Max(0, ActualWidth - BodyInset * 2), double.PositiveInfinity));
        to = expanded ? content.DesiredSize.Height + BodyInset * 2 : 0;
        body.MaxHeight = from;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        timer.Start();
    }

    void Finish()
    {
        timer.Stop();
        body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        body.MaxHeight = double.PositiveInfinity;
    }

    ExpandCollapseState State => expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    protected override AutomationPeer OnCreateAutomationPeer() => new SectionPeer(this);

    sealed class SectionPeer(KronoSection section) : FrameworkElementAutomationPeer(section), IExpandCollapseProvider
    {
        protected override string GetClassNameCore() => nameof(KronoSection);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => section.State;
        public void Expand() => section.IsExpanded = true;
        public void Collapse() => section.IsExpanded = false;
    }
}
