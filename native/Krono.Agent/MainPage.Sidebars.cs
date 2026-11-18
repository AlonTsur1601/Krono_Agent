using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Krono_Agent;

public sealed partial class MainPage
{
    FrameworkElement? chatHistory;
    string activeSection = "chats";
    bool sectionChangedThisDispatch;
    bool contextOpen = true;
    Storyboard? contextMotion;

    void ContextRailSizeChanged(object sender, SizeChangedEventArgs args)
        => ContextRail.Clip = new RectangleGeometry { Rect = new(0, 0, args.NewSize.Width, args.NewSize.Height) };

    void ShowContext(FrameworkElement? content)
    {
        ContextBody.Content = content;
        if (content is not null) KronoDesign.RefreshTypography(content);
        RefreshContextVisibility();
    }

    void RefreshContextVisibility()
    {
        var show = contextOpen && ContextBody.Content is not null;
        var target = show ? 252d : 0d;
        var current = ContextRail.ActualWidth;
        contextMotion?.Stop();
        contextMotion = null;
        ContextRail.Width = target;
        if (ContextBody.Content is null || !new UISettings().AnimationsEnabled || Math.Abs(current - target) < 1) {
            ContextRail.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        ContextRail.Visibility = Visibility.Visible;
        var slide = new DoubleAnimation {
            From = current, To = target, Duration = new Duration(KronoDesign.PageMotion),
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, ContextRail);
        Storyboard.SetTargetProperty(slide, "Width");
        var motion = new Storyboard();
        motion.Children.Add(slide);
        contextMotion = motion;
        motion.Completed += (_, _) => {
            if (contextMotion != motion) return;
            motion.Stop();
            ContextRail.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            contextMotion = null;
        };
        motion.Begin();
    }

    void InitializeNavigationIconMotion()
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()) {
            if (item.Tag is "chats" or "settings")
                AutomationProperties.SetAutomationId(item, "Nav-" + item.Tag);
            var pointerInside = false;
            var pressing = false;
            void UpdateScale() => AnimateIcon(item.Icon, pressing && pointerInside ? .92f : pointerInside ? 1.12f : 1);
            void EndPress(object _, PointerRoutedEventArgs args)
            {
                pressing = false;
                UpdateScale();
            }
            item.PointerEntered += (_, _) => {
                pointerInside = true;
                UpdateScale();
            };
            item.PointerExited += (_, _) => {
                pointerInside = false;
                pressing = false;
                UpdateScale();
            };
            item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => {
                if (!args.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
                pressing = true;
                UpdateScale();
            }), true);
            item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPress), true);
            item.PointerCaptureLost += EndPress;
            item.PointerCanceled += EndPress;
            item.Unloaded += (_, _) => {
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
                visual.Scale = Vector3.One;
                return;
            }
            visual.CenterPoint = new Vector3(visual.Size / 2, 0);
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(1, new Vector3(scale, scale, 1));
            animation.Duration = TimeSpan.FromMilliseconds(120);
            visual.StartAnimation("Scale", animation);
        }
    }

}
