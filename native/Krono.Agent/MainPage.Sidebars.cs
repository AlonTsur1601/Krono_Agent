using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
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
    InsetClip? contextClip;

    void ShowContext(FrameworkElement? content)
    {
        ContextBody.Content = content;
        ContextScroll.VerticalScrollBarVisibility = ReferenceEquals(content, chatHistory) && content is not null
            ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (content is not null) KronoDesign.RefreshTypography(content);
        RefreshContextVisibility();
    }

    void RefreshContextVisibility()
    {
        var show = contextOpen && ContextBody.Content is not null;
        if (show == contextVisible && (show || ContextRail.Visibility == Visibility.Collapsed)) return;
        if (show == contextVisible && ContextBody.Content is not null) return;
        contextVisible = show;
        var transition = ++contextTransition;
        var visual = ElementCompositionPreview.GetElementVisual(ContextRail);
        contextClip ??= visual.Compositor.CreateInsetClip();
        visual.Clip = contextClip;
        contextClip.StopAnimation("RightInset");

        void CommitLayout(bool visible)
        {
            ContextRail.Width = visible ? 252 : 0;
            ContextRail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceSurface.CornerRadius = new CornerRadius(visible ? 0 : 14, 0, 0, 0);
        }

        ContextRail.IsHitTestVisible = show;
        if (activeSection != "chats" || ContextBody.Content is null || !new UISettings().AnimationsEnabled) {
            contextClip.RightInset = 0;
            CommitLayout(show);
            return;
        }

        if (show) CommitLayout(true);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(135);
        var ease = visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(0, 1));
        animation.InsertKeyFrame(0, show ? 252 : 0);
        animation.InsertKeyFrame(1, show ? 0 : 252, ease);
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => {
            if (transition != contextTransition) return;
            if (!show) CommitLayout(false);
            contextClip.RightInset = 0;
        };
        contextClip.StartAnimation("RightInset", animation);
        batch.End();
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
                AnimateIcon(item.Icon, pointerInside ? .67f : 1);
            };
            void UpdateScale()
            {
                if (pressing) {
                    pressTimer.Stop();
                    AnimateIcon(item.Icon, .42f);
                    pressTimer.Start();
                } else if (!pressTimer.IsEnabled) AnimateIcon(item.Icon, pointerInside ? .67f : 1);
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

        static void AnimateIcon(IconElement? icon, float opacity)
        {
            if (icon is null) return;
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            if (!new UISettings().AnimationsEnabled) {
                visual.StopAnimation("Opacity");
                visual.Opacity = 1;
                return;
            }
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1, opacity);
            animation.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Opacity", animation);
        }
    }

}
