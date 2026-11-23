using Microsoft.UI.Xaml;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Krono_Agent;

public sealed partial class MainPage
{
    const int ProjectChatLimit = 5;
    readonly HashSet<string> collapsedChatProjects = new(StringComparer.Ordinal);
    readonly HashSet<string> expandedChatProjects = new(StringComparer.Ordinal);
    KronoTextBox? chatSearchInput;
    bool loadingChatHistory, chatArchiveOpen;

    enum RailKind { Heading, Disclosure, More, Chat }

    sealed record RailEntry(RailKind Kind, string Name, string Id = "", bool Indented = false,
        bool Pinned = false, bool Archived = false, bool Expanded = false, bool Project = false, string ProjectName = "", bool Working = false)
    {
        public Thickness Indent => new(Indented ? 16 : 0, 0, 0, 0);
        public string ActionLabel => Kind == RailKind.Disclosure
            ? (Expanded ? "Collapse " : "Expand ") + Name
            : Name + " in " + ProjectName;
        public Visibility PinVisibility => Archived ? Visibility.Collapsed : Visibility.Visible;
        public Visibility WorkingVisibility => Working ? Visibility.Visible : Visibility.Collapsed;
        public string PinLabel => Pinned ? "Unpin chat" : "Pin chat";
        public FrameworkElement PinIcon => KronoDesign.OutlineIcon(Pinned ? KronoDesign.OutlineGlyph.Unpin : KronoDesign.OutlineGlyph.Pin);
        public string ArchiveLabel => Archived ? "Restore chat" : "Archive chat";
        public FrameworkElement ArchiveIcon => KronoDesign.OutlineIcon(Archived ? KronoDesign.OutlineGlyph.Restore : KronoDesign.OutlineGlyph.Archive);
        public FrameworkElement? Icon => Project ? KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Folder) : null;
    }

    sealed class ChatRailTemplates(ResourceDictionary resources) : DataTemplateSelector
    {
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
            => (DataTemplate)resources[item is RailEntry entry ? entry.Kind switch {
                RailKind.Heading => "ChatRailHeading",
                RailKind.Disclosure => "ChatRailDisclosure",
                RailKind.More => "ChatRailMore",
                _ => "ChatRailConversation"
            } : "ChatRailConversation"];
    }

    void InitializeChatRail()
    {
        AutomationProperties.SetAutomationId(chatList, "ChatList");
        chatList.ItemTemplateSelector = new ChatRailTemplates(Resources);
        chatList.SelectionChanged += (_, _) => {
            if (loadingChatHistory || runId is not null) return;
            if (chatList.SelectedItem is not RailEntry { Kind: RailKind.Chat } chat) {
                SelectVisibleChat();
                return;
            }
            chatId = chat.Id;
            if (!ReferenceEquals(Body.Content, chatView)) ShowChats();
            else LoadChat();
        };
        chatList.ContainerContentChanging += (_, args) => {
            if (args.InRecycleQueue || args.ItemContainer is not ListViewItem row || args.Item is not RailEntry entry)
                return;
            row.MinHeight = 32;
            row.Padding = entry.Kind == RailKind.Disclosure ? new Thickness(0) : new Thickness(7, 0, 5, 0);
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.IsTabStop = entry.Kind == RailKind.Chat;
            row.IsHitTestVisible = entry.Kind != RailKind.Heading;
            AutomationProperties.SetName(row, entry.Name);
            if (entry.Kind != RailKind.Chat) {
                row.ContextFlyout = null;
                return;
            }
            if (row.ContextFlyout is not null) return;
            var menu = KronoDesign.CreateMenu();
            var pin = new MenuFlyoutItem();
            var archive = new MenuFlyoutItem();
            var delete = new MenuFlyoutItem { Text = "Delete chat" };
            menu.Items.Add(pin);
            menu.Items.Add(archive);
            menu.Items.Add(delete);
            menu.Opening += (_, _) => {
                if (chatList.ItemFromContainer(row) is not RailEntry chat) return;
                pin.Text = chat.PinLabel;
                pin.Visibility = chat.PinVisibility;
                archive.Text = chat.ArchiveLabel;
            };
            pin.Click += (_, _) => {
                if (chatList.ItemFromContainer(row) is RailEntry chat) ToggleChatPin(chat);
            };
            archive.Click += async (_, _) => {
                if (chatList.ItemFromContainer(row) is RailEntry chat) await ToggleChatArchive(chat);
            };
            delete.Click += async (_, _) => {
                if (chatList.ItemFromContainer(row) is not RailEntry chat) return;
                try { await DeleteChat(chat.Id); } catch (Exception error) { Error(error); }
            };
            row.ContextFlyout = menu;
        };
    }

    void SelectVisibleChat()
    {
        loadingChatHistory = true;
        try {
            chatList.SelectedItem = chatList.Items.OfType<RailEntry>()
                .FirstOrDefault(entry => entry.Kind == RailKind.Chat && entry.Id == chatId);
        } finally { loadingChatHistory = false; }
    }

    static void SetChatRowActions(object sender, Visibility visibility)
    {
        if (sender is Grid grid && grid.Children.LastOrDefault() is StackPanel actions)
            actions.Visibility = visibility;
    }

    void ChatRowPointerEntered(object sender, PointerRoutedEventArgs args) => SetChatRowActions(sender, Visibility.Visible);
    void ChatRowPointerExited(object sender, PointerRoutedEventArgs args) => SetChatRowActions(sender, Visibility.Collapsed);

    void ChatRailDisclosureClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RailEntry entry }) return;
        if (entry.Archived) chatArchiveOpen = !chatArchiveOpen;
        else if (!collapsedChatProjects.Add(entry.Id)) collapsedChatProjects.Remove(entry.Id);
        ReloadChats();
    }

    void ChatRailMoreClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RailEntry entry }) return;
        if (!expandedChatProjects.Add(entry.Id)) expandedChatProjects.Remove(entry.Id);
        ReloadChats();
    }

    void ChatPinClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: RailEntry chat }) ToggleChatPin(chat);
    }

    void ToggleChatPin(RailEntry chat)
    {
        if (runId is not null) return;
        workspace.Execute("UPDATE chats SET pinned=1-pinned, updated_at=$p0 WHERE id=$p1 AND deleted_at IS NULL AND archived=0",
            Workspace.Now, chat.Id);
        ReloadChats();
    }

    async void ChatArchiveClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: RailEntry chat }) await ToggleChatArchive(chat);
    }

    async Task ToggleChatArchive(RailEntry chat)
    {
        var account = workspace.AccountId;
        if (runId is not null || scheduledRun?.Chat == chat.Id) return;
        try {
            if (!chat.Archived && !await Confirm("Archive this chat?",
                "You can restore the conversation from Archived.", "Move to archive")) return;
            if (workspace.AccountId != account || runId is not null || scheduledRun?.Chat == chat.Id) return;
            workspace.Execute("UPDATE chats SET archived=$p0, updated_at=$p1 WHERE id=$p2 AND deleted_at IS NULL",
                chat.Archived ? 0 : 1, Workspace.Now, chat.Id);
            if (!chat.Archived && chatId == chat.Id) {
                chatId = null;
                draftProject = null;
                LoadChat();
            }
            ReloadChats();
        } catch (Exception error) { Error(error); }
    }

    void ReloadChats()
    {
        var search = chatSearchInput?.Text.Trim() ?? "";
        var searching = search.Length > 0;
        var rows = workspace.Query("""
            SELECT c.id, c.name, p.id AS project_id, p.name AS project_name, c.pinned, c.archived
            FROM chats c
            LEFT JOIN projects p ON p.id = c.project_id AND p.deleted_at IS NULL
            WHERE c.deleted_at IS NULL AND NOT EXISTS (SELECT 1 FROM side_chats side WHERE side.chat_id = c.id)
            ORDER BY c.updated_at DESC, c.id
            """).Where(row => row["name"]!.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        var entries = new List<RailEntry>();
        bool Pinned(JsonObject row) => row["pinned"]?.GetValue<long>() == 1;
        bool Archived(JsonObject row) => row["archived"]?.GetValue<long>() == 1;
        RailEntry Chat(JsonObject row, bool indent = false)
            => new(RailKind.Chat, row["name"]!.ToString(), row["id"]!.ToString(), indent, Pinned(row), Archived(row),
                Working: row["id"]?.ToString() == (scheduledRun?.Chat ?? (runId is not null ? activityChatId : null)));

        var pinned = rows.Where(row => !Archived(row) && Pinned(row)).ToArray();
        if (pinned.Length > 0) {
            entries.Add(new(RailKind.Heading, "Pinned"));
            entries.AddRange(pinned.Select(row => Chat(row)));
        }
        var unpinned = rows.Where(row => !Archived(row) && !Pinned(row)).ToArray();
        foreach (var project in unpinned.Where(row => row["project_id"] is not null)
            .GroupBy(row => (Id: row["project_id"]!.ToString(), Name: row["project_name"]!.ToString()))
            .OrderBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase)) {
            var expanded = !collapsedChatProjects.Contains(project.Key.Id);
            entries.Add(new(RailKind.Disclosure, project.Key.Name, project.Key.Id, Expanded: expanded, Project: true));
            if (!expanded) continue;
            var showAll = searching || expandedChatProjects.Contains(project.Key.Id);
            entries.AddRange((showAll ? project : project.Take(ProjectChatLimit)).Select(row => Chat(row, true)));
            if (!searching && project.Count() > ProjectChatLimit)
                entries.Add(new(RailKind.More, showAll ? "Show less" : "Show more", project.Key.Id, ProjectName: project.Key.Name));
        }
        var recent = unpinned.Where(row => row["project_id"] is null).ToArray();
        if (recent.Length > 0) {
            entries.Add(new(RailKind.Heading, "Recent"));
            entries.AddRange(recent.Select(row => Chat(row)));
        }
        var archived = rows.Where(Archived).ToArray();
        if (archived.Length > 0) {
            entries.Add(new(RailKind.Disclosure, "Archived", Archived: true, Expanded: chatArchiveOpen));
            if (chatArchiveOpen) entries.AddRange(archived.Select(row => Chat(row)));
        }

        var scroll = KronoInputMotion.Descendants(chatList).OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.VerticalOffset ?? 0;
        loadingChatHistory = true;
        try { chatList.ItemsSource = entries; }
        finally { loadingChatHistory = false; }
        SelectVisibleChat();
        if (scroll is not null && offset > 0)
            DispatcherQueue.TryEnqueue(() => scroll.ChangeView(null, offset, null, true));
    }
}
