using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
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
    Storyboard? contextWidthMotion;
    bool clearContextWhenHidden;

    void ShowContext(FrameworkElement? content)
    {
        var replacing = content is not null && contextVisible && !ReferenceEquals(ContextBody.Content, content);
        if (content is not null) ContextBody.Content = content;
        ContextScroll.VerticalScrollBarVisibility = ReferenceEquals(content, chatHistory) && content is not null
            ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (content is not null) KronoDesign.RefreshTypography(content);
        RefreshContextVisibility(content is not null);
        if (replacing && new UISettings().AnimationsEnabled) AnimateContextContent();
    }

    void AnimateContextContent()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(ContextScroll, true);
        var visual = ElementCompositionPreview.GetElementVisual(ContextScroll);
        var motion = visual.Compositor.CreateVector3KeyFrameAnimation();
        motion.InsertKeyFrame(0, new(-25, 0, 0));
        motion.InsertKeyFrame(1, System.Numerics.Vector3.Zero);
        motion.Duration = TimeSpan.FromMilliseconds(155);
        visual.StartAnimation("Translation", motion);
    }

    void RefreshContextVisibility(bool? hasContent = null)
    {
        if (hasContent is not null) clearContextWhenHidden = hasContent == false;
        var show = contextOpen && (hasContent ?? ContextBody.Content is not null);
        if (show == contextVisible) {
            if (hasContent == false && ContextRail.Visibility == Visibility.Collapsed) ContextBody.Content = null;
            return;
        }
        contextVisible = show;
        var transition = ++contextTransition;
        var wasVisible = ContextRail.Visibility == Visibility.Visible;
        var startWidth = wasVisible ? ContextRail.Width : 0;
        contextWidthMotion?.Stop();
        ContextRail.Width = startWidth;
        var visual = ElementCompositionPreview.GetElementVisual(ContextScroll);
        ElementCompositionPreview.SetIsTranslationEnabled(ContextScroll, true);
        var railVisual = ElementCompositionPreview.GetElementVisual(ContextRail);
        railVisual.Clip ??= railVisual.Compositor.CreateInsetClip();

        ContextRail.IsHitTestVisible = show;
        if (show) {
            ContextRail.Visibility = Visibility.Visible;
            WorkspaceSurface.CornerRadius = new CornerRadius(0);
        }
        if (new UISettings().AnimationsEnabled && (show || wasVisible)) {
            const int duration = 160;
            var motion = visual.Compositor.CreateVector3KeyFrameAnimation();
            if (show && !wasVisible) motion.InsertKeyFrame(0, new(-24, 0, 0));
            else motion.InsertExpressionKeyFrame(0, "this.StartingValue");
            motion.InsertKeyFrame(1, new(show ? 0 : -24, 0, 0),
                visual.Compositor.CreateCubicBezierEasingFunction(new(.18f, .8f), new(.3f, 1)));
            motion.Duration = TimeSpan.FromMilliseconds(duration);
            visual.StartAnimation("Translation", motion);
            var width = new DoubleAnimation {
                From = startWidth, To = show ? 252 : 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                EnableDependentAnimation = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(width, ContextRail);
            Storyboard.SetTargetProperty(width, "Width");
            storyboard.Children.Add(width);
            contextWidthMotion = storyboard;
            storyboard.Completed += (_, _) => Finish();
            storyboard.Begin();
            return;
        }
        Finish();

        void Finish()
        {
            if (transition != contextTransition) return;
            contextWidthMotion?.Stop();
            contextWidthMotion = null;
            ContextRail.Width = show ? 252 : 0;
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
            if (!show) {
                ContextRail.Visibility = Visibility.Collapsed;
                WorkspaceSurface.CornerRadius = new CornerRadius(14, 0, 0, 0);
                if (clearContextWhenHidden) ContextBody.Content = null;
            }
        }
    }

    void InitializeNavigationIconMotion()
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()) {
            if (item.Tag is "chats" or "settings")
                AutomationProperties.SetAutomationId(item, "Nav-" + item.Tag);
            if (item.Icon is PathIcon icon) {
                icon.Width = icon.Height = 20;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
            }
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
