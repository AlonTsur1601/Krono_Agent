using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class MainPage
{
    FrameworkElement? chatHistory;
    string activeSection = "chats";
    bool sectionChangedThisDispatch;
    bool contextOpen = true;
    bool contextVisible;
    int contextTransition;
    Storyboard? contextMotion;

    void ShowContext(FrameworkElement? content)
    {
        if (content is not null) ContextBody.Content = content;
        ContextScroll.VerticalScrollBarVisibility = ReferenceEquals(content, chatHistory) && content is not null
            ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (content is not null) KronoDesign.RefreshTypography(content);
        RefreshContextVisibility(content is not null);
    }

    void RefreshContextVisibility(bool? hasContent = null)
    {
        var show = contextOpen && (hasContent ?? ContextBody.Content is not null);
        if (show == contextVisible) {
            if (hasContent == false && ContextRail.Visibility == Visibility.Collapsed) ContextBody.Content = null;
            return;
        }
        contextVisible = show;
        var transition = ++contextTransition;
        var from = ContextRail.Visibility == Visibility.Visible ? ContextRail.ActualWidth : 0;
        contextMotion?.Stop();
        ContextRail.Width = from;

        void CommitLayout(bool visible)
        {
            contextMotion?.Stop();
            ContextRail.Width = visible ? 252 : 0;
            ContextRail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceSurface.CornerRadius = new CornerRadius(visible ? 0 : 14, 0, 0, 0);
            if (!visible && hasContent == false) ContextBody.Content = null;
        }

        ContextRail.IsHitTestVisible = show;
        if (!new UISettings().AnimationsEnabled || from == (show ? 252 : 0)) {
            CommitLayout(show);
            return;
        }

        ContextRail.Visibility = Visibility.Visible;
        WorkspaceSurface.CornerRadius = new CornerRadius(0);
        var animation = new DoubleAnimation { From = from, To = show ? 252 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(165)), EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, ContextRail);
        Storyboard.SetTargetProperty(animation, "Width");
        contextMotion = new Storyboard { Children = { animation } };
        contextMotion.Completed += (_, _) => { if (transition == contextTransition) CommitLayout(show); };
        var clip = ContextRail.Clip as RectangleGeometry;
        if (clip is null) {
            clip = new RectangleGeometry();
            ContextRail.Clip = clip;
            var railClip = clip;
            ContextRail.SizeChanged += (_, args) => railClip.Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        }
        contextMotion.Begin();
    }

    void InitializeNavigationIconMotion()
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()) {
            if (item.Tag is "chats" or "settings")
                AutomationProperties.SetAutomationId(item, "Nav-" + item.Tag);
            var pointerInside = false;
            var pressing = false;
            var pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            pressTimer.Tick += (_, _) => {
                pressTimer.Stop();
                AnimateIcon(item.Icon, pointerInside ? 1.13f : 1);
            };
            void UpdateScale()
            {
                if (pressing) {
                    pressTimer.Stop();
                    AnimateIcon(item.Icon, .86f);
                    pressTimer.Start();
                } else if (!pressTimer.IsEnabled) AnimateIcon(item.Icon, pointerInside ? 1.13f : 1);
            }
            void EndPress(object _, PointerRoutedEventArgs args)
            {
                pressing = false;
                UpdateScale();
            }
            item.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) => {
                pointerInside = true;
                UpdateScale();
            }), true);
            item.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => {
                pointerInside = false;
                pressing = false;
                UpdateScale();
            }), true);
            item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => {
                if (!args.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
                pressing = true;
                UpdateScale();
            }), true);
            item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPress), true);
            item.PointerCaptureLost += EndPress;
            item.PointerCanceled += EndPress;
            item.Unloaded += (_, _) => {
                pressTimer.Stop();
                pointerInside = pressing = false;
                UpdateScale();
            };
        }

        static void AnimateIcon(IconElement? icon, float scale)
        {
            if (icon is null) return;
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            if (!new UISettings().AnimationsEnabled) {
                visual.StopAnimation("Scale");
                visual.Scale = System.Numerics.Vector3.One;
                return;
            }
            visual.CenterPoint = new System.Numerics.Vector3(icon.ActualSize / 2, 0);
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, new(scale, scale, 1));
            animation.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Scale", animation);
        }
    }

}
