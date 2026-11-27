using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly ConditionalWeakTable<Grid, object> dragRows = new();
    readonly Dictionary<FrameworkElement, Func<RailEntry?>> orderRows = new();
    FrameworkElement? dragOrigin, dropRow;
    RailEntry? draggedEntry;
    Point dragStart;
    uint dragPointer;
    int dragGeneration;
    int orderGesture;
    bool draggingOrder, orderPointerHandlersAttached;
    readonly Border orderMarker = new() { Height = 2, IsHitTestVisible = false };
    readonly Border orderGhost = new() { CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
    Point latestOrderPointer;
    readonly DispatcherTimer orderMouseTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    int orderScreenStartY;
    bool trackingOrderMouse;

    [StructLayout(LayoutKind.Sequential)]
    struct OrderScreenPoint { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out OrderScreenPoint point);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    const string ProjectChatOrderMode = "native.projectChatOrderMode";
    bool ManualProjectChats => workspace.Setting(ProjectChatOrderMode) == "manual";

    Dictionary<string, int> OrderPositions(string key) => workspace.Setting(key)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
        .Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);

    void SetProjectChatOrder(string mode)
    {
        if (workspace.Setting(ProjectChatOrderMode) == mode) return;
        if (mode == "manual")
            workspace.SetSetting("native.chatOrder", string.Join(" ", ChatOrderRows().Select(row => row["id"]!.ToString())));
        workspace.SetSetting(ProjectChatOrderMode, mode);
        ReloadChats();
    }

    void RailOrderRowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Grid row || dragRows.TryGetValue(row, out _)) return;
        dragRows.Add(row, new object());
        EnableOrderDrag(row, () => row.DataContext as RailEntry);
    }

    void EnableOrderDrag(FrameworkElement element, Func<RailEntry?> entry)
    {
        orderRows[element] = entry;
        element.Loaded += (_, _) => orderRows[element] = entry;
        element.Unloaded += (_, _) => {
            orderRows.Remove(element);
            if (dragOrigin == element || dropRow == element) ClearOrderDrag();
        };
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => {
            if (runId is not null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                entry() is not { } source || !source.Project && source.Kind != RailKind.Chat) return;
            if (source.Kind == RailKind.Chat && source.ProjectId is not null && !ManualProjectChats) return;
            for (var child = args.OriginalSource as DependencyObject; child is not null && child != element;
                child = VisualTreeHelper.GetParent(child))
                if (child is Button { Name: not ("RailDisclosureButton" or "ProjectOpenButton") }) return;
            ClearOrderDrag();
            dragOrigin = element;
            draggedEntry = source;
            dragStart = args.GetCurrentPoint(this).Position;
            orderOriginTop = element.TransformToVisual(this).TransformPoint(default).Y;
            dragPointer = args.Pointer.PointerId;
            dragGeneration = sessionGeneration;
            if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && GetCursorPos(out var start)) {
                trackingOrderMouse = true;
                orderScreenStartY = start.Y;
                orderMouseTimer.Start();
            }
        }), true);
        if (orderPointerHandlersAttached) return;
        orderPointerHandlersAttached = true;
        orderMouseTimer.Tick += (_, _) => {
            if (!trackingOrderMouse || dragOrigin is null || !GetCursorPos(out var cursor)) return;
            var point = new Point(dragStart.X, dragStart.Y + (cursor.Y - orderScreenStartY) / XamlRoot.RasterizationScale);
            if ((GetAsyncKeyState(0x01) & 0x8000) == 0) {
                FinishOrderDrag(point);
                return;
            }
            PreviewOrderDrag(point);
        };
        AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) => {
            if (trackingOrderMouse || dragOrigin is null || dragPointer != args.Pointer.PointerId) return;
            var point = args.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed || runId is not null || dragGeneration != sessionGeneration) {
                ClearOrderDrag();
                return;
            }
            if (!draggingOrder && PassedDragThreshold(point.Position)) CapturePointer(args.Pointer);
            PreviewOrderDrag(point.Position);
            if (draggingOrder) args.Handled = true;
        }), true);
        AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) => {
            if (dragOrigin is null || dragPointer != args.Pointer.PointerId) return;
            var point = args.GetCurrentPoint(this).Position;
            var hadCapture = draggingOrder && !trackingOrderMouse;
            if (FinishOrderDrag(point)) args.Handled = true;
            if (hadCapture) ReleasePointerCapture(args.Pointer);
        }), true);
        PointerCaptureLost += (_, args) => { if (!trackingOrderMouse && ReferenceEquals(args.OriginalSource, this)) ClearOrderDrag(); };
        PointerCanceled += (_, _) => { if (!trackingOrderMouse) ClearOrderDrag(); };
    }

    void PreviewOrderDrag(Point point)
    {
        if (runId is not null || dragGeneration != sessionGeneration) { ClearOrderDrag(); return; }
        if (dragOrigin is not { } origin || !draggingOrder && !PassedDragThreshold(point)) return;
        latestOrderPointer = point;
        if (!draggingOrder) {
            draggingOrder = true;
            ShowOrderGhost(origin);
        }
        var (row, _, before, offset) = FindOrderTarget(point);
        if (orderGhost.Child is not null) Canvas.SetTop(orderGhost, orderOriginTop + offset);
        else TranslateOrderRow(origin, offset);
        ShowOrderMarker(row, before);
    }

    bool FinishOrderDrag(Point point)
    {
        var moved = draggingOrder || PassedDragThreshold(point);
        if (moved) orderGesture++;
        var source = draggedEntry;
        var (_, target, before, _) = FindOrderTarget(point);
        var generation = dragGeneration;
        ClearOrderDrag();
        if (moved && source is not null && target is not null && runId is null)
            DispatcherQueue.TryEnqueue(() => {
                if (generation != sessionGeneration) return;
                try { ReorderEntry(source, target, before); } catch (Exception error) { Error(error); }
            });
        return moved;
    }

    bool PassedDragThreshold(Point point) => Math.Abs(point.Y - dragStart.Y) >= 5;

    (Grid? Row, RailEntry? Entry, bool Before, float Offset) FindOrderTarget(Point pointer)
    {
        if (draggedEntry is not { } source || dragOrigin is not { } origin) return default;
        bool OnRail(FrameworkElement row) => row is Grid grid && dragRows.TryGetValue(grid, out _);
        var rail = OnRail(origin);
        var rows = new List<(Grid Row, RailEntry Entry, double Top, double Height)>();
        foreach (var (element, getEntry) in orderRows) {
            if (element is not Grid row || !row.IsLoaded || row.Visibility != Visibility.Visible || row.ActualHeight <= 0 ||
                OnRail(row) != rail || getEntry() is not { } target) continue;
            if (!(source.Project && target.Project || source.Kind == RailKind.Chat && target.Kind == RailKind.Chat &&
                source.ProjectId == target.ProjectId && source.Pinned == target.Pinned && source.Archived == target.Archived)) continue;
            // Layout coordinates stay fixed while the composition visual follows the pointer.
            var top = row.TransformToVisual(this).TransformPoint(default).Y;
            if (row == origin) top = orderOriginTop;
            rows.Add((row, target, top, row.ActualHeight));
        }
        rows.Sort((a, b) => a.Top.CompareTo(b.Top));
        var from = rows.FindIndex(row => row.Row == origin);
        if (from < 0 || rows.Count < 2) return default;
        var current = rows[from];
        var delta = Math.Clamp(pointer.Y - dragStart.Y, rows[0].Top - current.Top,
            Math.Max(0, rows[^1].Top + rows[^1].Height - current.Top - current.Height));
        var slotY = current.Top + current.Height / 2 + delta;
        var destination = from;
        if (delta < 0) {
            while (destination > 0 && slotY <= rows[destination - 1].Top + rows[destination - 1].Height / 2) destination--;
        } else if (delta > 0) {
            while (destination < rows.Count - 1 && slotY >= rows[destination + 1].Top + rows[destination + 1].Height / 2) destination++;
        }
        // Clamp extreme drags all the way to the first or last slot, even for unequal row heights.
        if (pointer.Y < rows[0].Top) destination = 0;
        if (pointer.Y > rows[^1].Top + rows[^1].Height) destination = rows.Count - 1;
        return destination == from ? (null, null, false, (float)delta) :
            (rows[destination].Row, rows[destination].Entry, destination < from, (float)delta);
    }

    double orderOriginTop;

    static void TranslateOrderRow(FrameworkElement row, float offset)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        ElementCompositionPreview.GetElementVisual(row).Properties.InsertVector3("Translation", new(0, offset, 0));
    }

    async void ShowOrderGhost(FrameworkElement row)
    {
        var gesture = orderGesture;
        var image = new RenderTargetBitmap();
        try { await image.RenderAsync(row); } catch (Exception) { return; }
        if (!draggingOrder || dragOrigin != row || gesture != orderGesture || image.PixelHeight == 0) return;
        TranslateOrderRow(row, 0);
        var position = row.TransformToVisual(this).TransformPoint(default);
        var (_, _, _, offset) = FindOrderTarget(latestOrderPointer);
        orderGhost.Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
        orderGhost.Width = row.ActualWidth;
        orderGhost.Height = row.ActualHeight;
        var preview = new Grid();
        preview.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        preview.Children.Add(new Border {
            Background = KronoDesign.ThemeBrush("KronoOutlineHover", row.ActualTheme),
            CornerRadius = orderGhost.CornerRadius, Opacity = .32
        });
        orderGhost.Child = preview;
        Canvas.SetLeft(orderGhost, position.X);
        Canvas.SetTop(orderGhost, orderOriginTop + offset);
        OrderOverlay.Children.Add(orderGhost);
        row.Opacity = .35;
    }

    void ShowOrderMarker(Grid? row, bool before)
    {
        if (row is null) {
            if (dropRow is not null) OrderOverlay.Children.Remove(orderMarker);
            dropRow = null;
            return;
        }
        if (dropRow is null) {
            orderMarker.Background = KronoDesign.ThemeBrush("KronoAccentForeground", row.ActualTheme);
            Canvas.SetZIndex(orderMarker, 1);
            OrderOverlay.Children.Add(orderMarker);
        }
        dropRow = row;
        var position = row.TransformToVisual(this).TransformPoint(default);
        orderMarker.Width = row.ActualWidth;
        Canvas.SetLeft(orderMarker, position.X);
        Canvas.SetTop(orderMarker, before ? position.Y : position.Y + row.ActualHeight - orderMarker.Height);
    }

    void ClearOrderDrag()
    {
        orderMouseTimer.Stop();
        trackingOrderMouse = false;
        if (draggingOrder && dragOrigin is not null) {
            TranslateOrderRow(dragOrigin, 0);
            dragOrigin.Opacity = 1;
        }
        OrderOverlay.Children.Remove(orderGhost);
        orderGhost.Child = null;
        ShowOrderMarker(null, false);
        dragOrigin = dropRow = null;
        draggedEntry = null;
        draggingOrder = false;
    }

    void ReorderEntry(RailEntry source, RailEntry target, bool before)
    {
        var key = source.Project ? "native.projectOrder" : "native.chatOrder";
        var ids = source.Project ? OrderedProjects().Select(project => project.Id).ToList() :
            ChatOrderRows().Select(row => row["id"]!.ToString()).ToList();
        if (!ids.Contains(target.Id) || !ids.Remove(source.Id)) return;
        ids.Insert(ids.IndexOf(target.Id) + (before ? 0 : 1), source.Id);
        workspace.SetSetting(key, string.Join(" ", ids));
        ReloadChats();
        if (source.Project) RefreshProjectRows();
    }

    async Task ChooseChatProject(string? target)
    {
        if (runId is not null || target is not null && scheduledRun?.Chat == target) return;
        var generation = sessionGeneration;
        var currentProject = target is null ? draftProject :
            workspace.Query("SELECT project_id FROM chats WHERE id=$p0", target).FirstOrDefault()?["project_id"]?.ToString();
        var projects = OrderedProjects();
        var selector = new KronoComboBox {
            MinWidth = 260,
            ItemsSource = new[] { "No project" }.Concat(projects.Select(project => project.Name)).ToArray(),
            SelectedIndex = projects.FindIndex(project => project.Id == currentProject) + 1,
        };
        AutomationProperties.SetAutomationId(selector, "ChatProjectSelector");
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = target is null ? "Choose project for new chat" : "Move chat to project",
            Content = selector, PrimaryButtonText = target is null ? "Choose" : "Move", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || generation != sessionGeneration ||
            runId is not null || (target is null ? chatId is not null : scheduledRun?.Chat == target)) return;
        var project = selector.SelectedIndex > 0 ? projects[selector.SelectedIndex - 1].Id : null;
        if (project == currentProject || project is not null && !OrderedProjects().Any(item => item.Id == project)) return;
        if (target is null) { draftProject = project; loadedContext = null; if (project is not null) RememberActiveProject(project); }
        else workspace.Execute("UPDATE chats SET project_id=$p0,updated_at=$p1 WHERE id=$p2 AND account_id=$p3 AND deleted_at IS NULL",
            project, Workspace.Now, target, workspace.AccountId);
        ReloadChats();
        if (target == chatId && ReferenceEquals(Body.Content, chatView)) LoadChat();
    }

    void NewRailProjectChat(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RailEntry { Project: true } project }) return;
        try { NewProjectChat(project.Id); } catch (Exception error) { Error(error); }
    }

    void NewProjectChat(string project)
    {
        if (runId is not null || !OrderedProjects().Any(item => item.Id == project)) return;
        RememberActiveProject(project);
        collapsedChatProjects.Remove(project);
        OpenDraft(project);
    }

    void OpenDraft(string? project)
    {
        CloseSideChat();
        draftProject = project;
        chatId = loadedContext = null;
        composer.Text = "";
        ClearAttachments();
        ShowChats();
        NativeMarkdown.Focus(composer);
    }
}
