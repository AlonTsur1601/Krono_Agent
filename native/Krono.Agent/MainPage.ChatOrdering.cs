using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    Dictionary<string, int> OrderPositions(string key) => workspace.Setting(key)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
        .Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);

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
            for (var child = args.OriginalSource as DependencyObject; child is not null && child != element;
                child = VisualTreeHelper.GetParent(child))
                if (child is Button { Name: not ("RailDisclosureButton" or "ProjectOpenButton") }) return;
            ClearOrderDrag();
            dragOrigin = element;
            draggedEntry = source;
            dragStart = args.GetCurrentPoint(this).Position;
            dragPointer = args.Pointer.PointerId;
            dragGeneration = sessionGeneration;
        }), true);
        if (orderPointerHandlersAttached) return;
        orderPointerHandlersAttached = true;
        AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) => {
            if (dragOrigin is null || dragPointer != args.Pointer.PointerId) return;
            var point = args.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed || runId is not null || dragGeneration != sessionGeneration) {
                ClearOrderDrag();
                return;
            }
            if (!draggingOrder) {
                if (!PassedDragThreshold(point.Position) || !CapturePointer(args.Pointer)) return;
                draggingOrder = true;
            }
            args.Handled = true;
            var (row, _, _) = FindOrderTarget(point.Position);
            if (row == dropRow) return;
            if (dropRow is not null) dropRow.Opacity = 1;
            dropRow = row;
            if (dropRow is not null) dropRow.Opacity = .72;
        }), true);
        AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) => {
            if (dragOrigin is null || dragPointer != args.Pointer.PointerId) return;
            var source = draggedEntry;
            var point = args.GetCurrentPoint(this).Position;
            var (_, target, before) = FindOrderTarget(point);
            var hadCapture = draggingOrder;
            var moved = PassedDragThreshold(point);
            if (moved) orderGesture++;
            var reorder = moved && source is not null && target is not null &&
                dragGeneration == sessionGeneration && runId is null;
            if (hadCapture || reorder) args.Handled = true;
            ClearOrderDrag();
            if (hadCapture) ReleasePointerCapture(args.Pointer);
            if (!reorder) return;
            var generation = sessionGeneration;
            DispatcherQueue.TryEnqueue(() => {
                if (generation != sessionGeneration) return;
                try { ReorderEntry(source!, target!, before); } catch (Exception error) { Error(error); }
            });
        }), true);
        PointerCaptureLost += (_, args) => { if (ReferenceEquals(args.OriginalSource, this)) ClearOrderDrag(); };
        PointerCanceled += (_, _) => ClearOrderDrag();
    }

    bool PassedDragThreshold(Point point) => Math.Abs(point.Y - dragStart.Y) >= 5 || Math.Abs(point.X - dragStart.X) >= 5;

    (FrameworkElement? Row, RailEntry? Entry, bool Before) FindOrderTarget(Point pointer)
    {
        if (draggedEntry is not { } source) return default;
        foreach (var (row, entry) in orderRows) {
            if (row == dragOrigin || !row.IsLoaded || row.Visibility != Visibility.Visible || row.ActualHeight <= 0 ||
                entry() is not { } target || source.Id == target.Id) continue;
            var rect = row.TransformToVisual(this).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
            if (!rect.Contains(pointer)) continue;
            if (source.Project && target.Project || source.Kind == RailKind.Chat && target.Kind == RailKind.Chat &&
                source.ProjectId == target.ProjectId && source.Pinned == target.Pinned && source.Archived == target.Archived)
                return (row, target, pointer.Y < rect.Top + rect.Height / 2);
            return default;
        }
        return default;
    }

    void ClearOrderDrag()
    {
        if (dropRow is not null) dropRow.Opacity = 1;
        dragOrigin = dropRow = null;
        draggedEntry = null;
        draggingOrder = false;
    }

    void ReorderEntry(RailEntry source, RailEntry target, bool before)
    {
        var key = source.Project ? "native.projectOrder" : "native.chatOrder";
        var positions = OrderPositions(key);
        var ids = source.Project ? OrderedProjects().Select(project => project.Id).ToList() :
            workspace.Query("""
                SELECT id FROM chats WHERE account_id=$p0 AND deleted_at IS NULL
                AND NOT EXISTS (SELECT 1 FROM side_chats WHERE chat_id=chats.id)
                ORDER BY updated_at DESC,id
                """, workspace.AccountId).Select(row => row["id"]!.ToString())
                .OrderBy(id => positions.GetValueOrDefault(id, -1)).ToList();
        if (!ids.Contains(target.Id) || !ids.Remove(source.Id)) return;
        ids.Insert(ids.IndexOf(target.Id) + (before ? 0 : 1), source.Id);
        workspace.SetSetting(key, string.Join(" ", ids));
        var overview = source.Project && ReferenceEquals(Body.Content, projectsOverview);
        ReloadChats();
        if (overview) ShowProjects();
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
        if (target is null) draftProject = project;
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
        var last = workspace.Setting("native.lastProjectPermission." + project);
        if (last.Length == 0) {
            var previous = workspace.Query("""
                SELECT chats.id FROM chats JOIN settings ON settings.key='native.chatOptions.' || chats.id
                WHERE account_id=$p0 AND project_id=$p1 AND deleted_at IS NULL ORDER BY updated_at DESC LIMIT 1
                """, workspace.AccountId, project).FirstOrDefault()?["id"]?.ToString();
            last = (previous is null ? null : workspace.GetChatOptions(previous)?.Permissions)
                ?? workspace.ProjectPreferences(project)["permissions"]?.ToString() ?? "ask";
        }
        var newChat = workspace.CreateChat(project);
        workspace.SaveChatOptions(newChat, new(provider, model, mode, last, workspace.Setting(ChatReasoningKey),
            workspace.Setting("native.chatContext"), workspace.Setting(ChatFastModeKey) == "true"));
        CloseSideChat();
        draftProject = project;
        chatId = newChat;
        collapsedChatProjects.Remove(project);
        ShowChats();
        NativeMarkdown.Focus(composer);
    }
}
