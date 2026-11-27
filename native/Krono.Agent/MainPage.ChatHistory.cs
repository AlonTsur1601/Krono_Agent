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
        bool Pinned = false, bool Archived = false, bool Expanded = false, bool Project = false, string ProjectName = "", bool Working = false, string? ProjectId = null)
    {
        public Thickness Indent => new(Indented ? 16 : 0, 0, 0, 0);
        public string ActionLabel => Kind == RailKind.Disclosure
            ? (Expanded ? "Collapse " : "Expand ") + Name
            : Name + " in " + ProjectName;
        public string NewChatLabel => "New chat in " + Name;
        public Visibility ProjectActionVisibility => Project ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PinVisibility => Archived ? Visibility.Collapsed : Visibility.Visible;
        public Visibility WorkingVisibility => Working ? Visibility.Visible : Visibility.Collapsed;
        public string PinLabel => Pinned ? "Unpin chat" : "Pin chat";
        public string ArchiveLabel => Archived ? "Restore chat" : "Archive chat";
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
        void PaintAccent() => KronoDesign.ApplyControlAccents(chatList);
        PaintAccent();
        chatList.Loaded += (_, _) => PaintAccent();
        chatList.ActualThemeChanged += (_, _) => PaintAccent();
        KronoInputMotion.RegisterPaletteRefresh(chatList, PaintAccent);
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
            row.MinHeight = 34;
            row.Margin = new Thickness(0, entry.Kind is RailKind.Heading or RailKind.Disclosure ? 6 : 1, 0, 1);
            row.Padding = entry.Kind == RailKind.Disclosure ? new Thickness(0) : new Thickness(13, 0, 6, 0);
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.IsTabStop = entry.Kind == RailKind.Chat || entry.Project;
            row.IsHitTestVisible = entry.Kind != RailKind.Heading;
            AutomationProperties.SetName(row, entry.Name);
            row.ContextFlyout = entry.Kind == RailKind.Chat ? ChatActions(entry) : null;
        };
    }

    MenuFlyout ChatActions(RailEntry chat)
    {
        var menu = KronoDesign.CreateMenu();
        var pin = new MenuFlyoutItem { Text = chat.PinLabel, Visibility = chat.PinVisibility, Icon = new SymbolIcon(Symbol.Pin) };
        var archive = new MenuFlyoutItem { Text = chat.ArchiveLabel, Icon = new SymbolIcon(Symbol.Folder) };
        var move = new MenuFlyoutItem { Text = "Move to project…", Icon = new SymbolIcon(Symbol.MoveToFolder) };
        var delete = new MenuFlyoutItem { Text = "Delete chat", Icon = new SymbolIcon(Symbol.Delete) };
        KronoDesign.MarkDestructive(delete);
        pin.Click += (_, _) => ToggleChatPin(chat);
        archive.Click += async (_, _) => await ToggleChatArchive(chat);
        move.Click += async (_, _) => {
            try { await ChooseChatProject(chat.Id); } catch (Exception error) { Error(error); }
        };
        delete.Click += async (_, _) => {
            try { await DeleteChat(chat.Id); } catch (Exception error) { Error(error); }
        };
        menu.Items.Add(pin);
        menu.Items.Add(archive);
        menu.Items.Add(move);
        menu.Items.Add(delete);
        return menu;
    }

    void ChatActionsClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: RailEntry chat } anchor)
            ChatActions(chat).ShowAt(anchor);
    }

    void LoadChatRailButton(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button) return;
        if (button.Name == "RailDisclosureButton")
            button.Template = (ControlTemplate)Application.Current.Resources["KronoFlatIconTemplate"];
        else if (button.Width == 28) KronoInputMotion.StyleButton(button);
        else KronoInputMotion.AttachFlatHover(button);
    }

    void SelectVisibleChat()
    {
        loadingChatHistory = true;
        try {
            chatList.SelectedItem = chatList.Items.OfType<RailEntry>()
                .FirstOrDefault(entry => entry.Kind == RailKind.Chat && entry.Id == chatId);
        } finally { loadingChatHistory = false; }
    }

    void ChatRailDisclosureClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RailEntry entry }) return;
        AfterOrderGesture(() => {
            if (entry.Archived) chatArchiveOpen = !chatArchiveOpen;
            else if (!collapsedChatProjects.Add(entry.Id)) collapsedChatProjects.Remove(entry.Id);
            ReloadChats();
        });
    }

    void AfterOrderGesture(Action action)
    {
        var gesture = orderGesture;
        var generation = sessionGeneration;
        DispatcherQueue.TryEnqueue(() => {
            if (gesture != orderGesture || generation != sessionGeneration) return;
            try { action(); } catch (Exception error) { Error(error); }
        });
    }

    void ChatRailMoreClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RailEntry entry }) return;
        if (!expandedChatProjects.Add(entry.Id)) expandedChatProjects.Remove(entry.Id);
        ReloadChats();
    }

    void ToggleChatPin(RailEntry chat)
    {
        if (runId is not null) return;
        workspace.Execute("UPDATE chats SET pinned=1-pinned, updated_at=$p0 WHERE id=$p1 AND deleted_at IS NULL AND archived=0",
            Workspace.Now, chat.Id);
        ReloadChats();
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

    List<JsonObject> ChatOrderRows()
    {
        var order = OrderPositions("native.chatOrder");
        var manual = ManualProjectChats;
        var rows = workspace.Query("""
            SELECT c.id, c.name, p.id AS project_id, c.pinned, c.archived, c.created_at
            FROM chats c
            LEFT JOIN projects p ON p.id = c.project_id AND p.deleted_at IS NULL
            WHERE c.account_id=$p0 AND c.deleted_at IS NULL AND NOT EXISTS (SELECT 1 FROM side_chats side WHERE side.chat_id = c.id)
            ORDER BY c.updated_at DESC, c.id
            """, workspace.AccountId);
        var sorted = rows.OrderBy(row => manual || row["project_id"] is null
            ? order.GetValueOrDefault(row["id"]!.ToString(), -1) : -1)
            .ThenByDescending(row => manual && row["project_id"] is not null && !order.ContainsKey(row["id"]!.ToString())
                ? row["created_at"]?.GetValue<long>() ?? 0 : 0).ToList();
        if (manual && sorted.Any(row => !order.ContainsKey(row["id"]!.ToString())))
            workspace.SetSetting("native.chatOrder", string.Join(" ", sorted.Select(row => row["id"]!.ToString())));
        return sorted;
    }

    void ReloadChats()
    {
        var search = chatSearchInput?.Text.Trim() ?? "";
        var searching = search.Length > 0;
        var rows = ChatOrderRows().Where(row => row["name"]!.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        var entries = new List<RailEntry>();
        bool Pinned(JsonObject row) => row["pinned"]?.GetValue<long>() == 1;
        bool Archived(JsonObject row) => row["archived"]?.GetValue<long>() == 1;
        RailEntry Chat(JsonObject row, bool indent = false)
            => new(RailKind.Chat, row["name"]!.ToString(), row["id"]!.ToString(), indent, Pinned(row), Archived(row),
                Working: row["id"]?.ToString() == (scheduledRun?.Chat ?? (runId is not null ? activityChatId : null)), ProjectId: row["project_id"]?.ToString());

        var pinned = rows.Where(row => !Archived(row) && Pinned(row)).ToArray();
        if (pinned.Length > 0) {
            entries.Add(new(RailKind.Heading, "Pinned"));
            entries.AddRange(pinned.Select(row => Chat(row)));
        }
        var unpinned = rows.Where(row => !Archived(row) && !Pinned(row)).ToArray();
        var grouped = unpinned.Where(row => row["project_id"] is not null).ToLookup(row => row["project_id"]!.ToString());
        foreach (var (id, name) in OrderedProjects()) {
            var project = grouped[id];
            if (searching && !project.Any()) continue;
            var expanded = !collapsedChatProjects.Contains(id);
            entries.Add(new(RailKind.Disclosure, name, id, Expanded: expanded, Project: true));
            if (!expanded) continue;
            var showAll = searching || expandedChatProjects.Contains(id);
            entries.AddRange((showAll ? project : project.Take(ProjectChatLimit)).Select(row => Chat(row, true)));
            if (!searching && project.Count() > ProjectChatLimit)
                entries.Add(new(RailKind.More, showAll ? "Show less" : "Show more", id, ProjectName: name));
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
