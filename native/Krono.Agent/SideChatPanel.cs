using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;

namespace Krono_Agent;

// Separate worker/run, immutable copied transcript, no tools or automatic memories.
public sealed class SideChatPanel : Grid, IDisposable
{
    readonly Workspace workspace;
    readonly AgentWorker worker = new();
    readonly Func<JsonObject> providerConfig;
    readonly Action close;
    readonly Action<string> openChat;
    readonly Action transferQueued;
    readonly StackPanel transcript = new() { Spacing = 14 };
    readonly ComboBox saved = new() { Header = "Temporary chats", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    readonly TextBlock expiry = new() { FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8 };
    readonly TextBox input = new() { PlaceholderText = "Ask in this side chat…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 120 };
    readonly ScrollViewer scroll;
    readonly Button send = new() { Content = "Send side message" }, stop = new() { Content = "Stop", IsEnabled = false };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    string chatId, parent = "", response = "";
    string? runId;
    TextBlock? streaming;
    ContentDialog? transferDialog;
    bool disposed;
    record SavedSide(string Id, string Name);
    public string ParentChatId => parent;
    public void FocusComposer() { if (!disposed) input.Focus(FocusState.Programmatic); }

    public SideChatPanel(Workspace workspace, string id, Func<JsonObject> providerConfig, Action close, Action<string> openChat, Action transferQueued)
    {
        this.workspace = workspace; chatId = id; this.providerConfig = providerConfig; this.close = close; this.openChat = openChat; this.transferQueued = transferQueued;
        RowSpacing = 12; foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock { Text = "Side chat", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(ActionButton("Parent chat", () => openChat(parent)));
        actions.Children.Add(ActionButton("Close side chat", close)); header.Children.Add(actions); header.Children.Add(saved); header.Children.Add(expiry);
        header.Children.Add(new TextBlock { Text = "Context snapshot · Chat only · No tools or automatic memories", FontSize = 12, Opacity = .6, TextWrapping = TextWrapping.Wrap }); Children.Add(header);
        scroll = new ScrollViewer { Content = transcript, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); Children.Add(scroll);
        Grid.SetRow(status, 2); Children.Add(status);
        var footer = new StackPanel { Spacing = 8 }; footer.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; buttons.Children.Add(send); buttons.Children.Add(stop); footer.Children.Add(buttons);
        footer.Children.Add(ActionButton("Keep as independent chat", Keep)); footer.Children.Add(ActionButton("Transfer latest reply…", Transfer)); Grid.SetRow(footer, 3); Children.Add(footer);
        AutomationProperties.SetAutomationId(input, "SideComposer"); AutomationProperties.SetAutomationId(send, "SideSend"); AutomationProperties.SetAutomationId(status, "SideStatus"); AutomationProperties.SetAutomationId(expiry, "SideExpiry"); AutomationProperties.SetAutomationId(saved, "SideChatSelector");
        send.Click += async (_, _) => await Send(); stop.Click += async (_, _) => { if (runId is not null) await worker.Send(Guid.NewGuid().ToString(), "cancel", new { id = runId }); };
        worker.Event += (requestId, type, data) => {
            if (disposed || requestId != runId || !Available()) return;
            if (type == "chunk" && data?["type"]?.ToString() == "text") { response += data["content"]?.ToString(); if (streaming is not null) streaming.Text = response; scroll.ChangeView(null, scroll.ScrollableHeight, null); }
        };
        saved.SelectionChanged += (_, _) => { if (runId is null && saved.SelectedItem is SavedSide side) { chatId = side.Id; Render(); } };
        RefreshList(); Render(); timer.Tick += (_, _) => UpdateExpiry(); timer.Start();
    }
    Button ActionButton(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => { try { action(); } catch (Exception error) { status.Text = error.Message; } }; return button; }
    Button ActionButton(string text, Func<Task> action) { var button = new Button { Content = text }; button.Click += async (_, _) => { try { await action(); } catch (Exception error) { if (!disposed) status.Text = error.Message; } }; return button; }
    bool Available() => workspace.Query("SELECT chat_id FROM side_chats WHERE chat_id=$p0 AND expires_at>$p1", chatId, Workspace.Now).Count > 0;
    void RefreshList()
    {
        var items = workspace.Query("SELECT c.id,c.name FROM chats c JOIN side_chats s ON s.chat_id=c.id WHERE s.expires_at>$p0 ORDER BY c.updated_at DESC", Workspace.Now).Select(row => new SavedSide(row["id"]!.ToString(), row["name"]!.ToString())).ToArray();
        saved.ItemsSource = items; saved.SelectedItem = items.FirstOrDefault(item => item.Id == chatId);
    }
    void Render()
    {
        transcript.Children.Clear(); if (!Available()) { UpdateExpiry(); return; }
        parent = workspace.Query("SELECT parent_chat_id FROM side_chats WHERE chat_id=$p0", chatId).First()["parent_chat_id"]!.ToString();
        foreach (var message in workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at,rowid", chatId)) {
            transcript.Children.Add(new TextBlock { Text = message["role"]?.ToString() == "user" ? "You" : "Krono", Opacity = .6, FontSize = 12 });
            transcript.Children.Add(NativeMarkdown.Render(message["content"]?.ToString() ?? "", KronoDesign.TextFont.Source, KronoDesign.TextSize, KronoDesign.CodeFont.Source, KronoDesign.CodeSize, workspace.Setting("native.codePalette")));
        }
        UpdateExpiry(); timer.Start(); DispatcherQueue.TryEnqueue(() => { if (!disposed) { scroll.UpdateLayout(); scroll.ChangeView(null, scroll.ScrollableHeight, null, true); } });
    }
    void UpdateExpiry()
    {
        if (disposed) return;
        var entry = workspace.Query("SELECT expires_at FROM side_chats WHERE chat_id=$p0", chatId).FirstOrDefault();
        var remaining = entry is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(entry["expires_at"]!.GetValue<long>() - Workspace.Now);
        if (remaining <= TimeSpan.Zero) {
            if (runId is not null) { _ = worker.Send(Guid.NewGuid().ToString(), "cancel", new { id = runId }); runId = null; }
            workspace.PurgeExpiredSideChats(); timer.Stop(); ClearPreview(); response = ""; if (streaming is not null) streaming.Text = ""; streaming = null;
            transcript.Children.Clear(); input.Text = ""; input.IsEnabled = send.IsEnabled = stop.IsEnabled = false; RefreshList();
            expiry.Text = "Expired · temporary messages deleted"; status.Text = "This side chat is no longer available."; return;
        }
        input.IsEnabled = true; send.IsEnabled = runId is null;
        expiry.Text = $"Expires in {(int)remaining.TotalHours}h {remaining.Minutes:00}m · 24h after the last message";
    }
    async Task Send()
    {
        if (disposed || runId is not null || string.IsNullOrWhiteSpace(input.Text) || !Available()) return;
        try {
            var request = providerConfig(); var message = input.Text; workspace.AddMessage(chatId, "user", message); input.Text = "";
            workspace.Execute("UPDATE chats SET name=$p0 WHERE id=$p1", "Side · " + message[..Math.Min(32, message.Length)], chatId);
            Render(); streaming = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = KronoDesign.TextFont, FontSize = KronoDesign.TextSize }; transcript.Children.Add(streaming); response = "";
            var context = JsonNode.Parse(workspace.Query("SELECT context FROM side_chats WHERE chat_id=$p0", chatId).First()["context"]!.ToString())!;
            request["mode"] = "chat"; request["permissions"] = "ask"; request["projectDirs"] = new JsonArray(); request["instructions"] = context["instructions"]?.ToString() + "\nThis is a temporary side conversation with a snapshot of its parent. No tools or automatic memory updates are available.";
            request["memories"] = context["memories"]?.DeepClone() ?? new JsonArray();
            request["messages"] = new JsonArray(workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at,rowid", chatId).Select(row => (JsonNode?)new JsonObject { ["role"] = row["role"]!.ToString(), ["content"] = row["content"]?.ToString() ?? "" }).ToArray());
            runId = Guid.NewGuid().ToString(); saved.IsEnabled = send.IsEnabled = false; stop.IsEnabled = true; status.Text = "Working independently…";
            await worker.Request(runId, "run", request);
            if (!disposed && Available()) { workspace.AddMessage(chatId, "assistant", response); status.Text = "Done"; }
        } catch (Exception error) { if (!disposed) status.Text = error.Message; }
        finally { runId = null; if (!disposed) { saved.IsEnabled = true; stop.IsEnabled = false; if (Available()) RefreshList(); Render(); } }
    }
    void Keep() { if (runId is not null || !Available()) return; workspace.KeepSideChat(chatId); openChat(chatId); close(); }
    async Task Transfer()
    {
        if (runId is not null || !Available()) return;
        var latest = workspace.Query("SELECT content FROM messages WHERE chat_id=$p0 AND role='assistant' AND deleted_at IS NULL ORDER BY created_at DESC,rowid DESC LIMIT 1", chatId).FirstOrDefault()?["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(latest)) { status.Text = "There is no assistant reply to transfer."; return; }
        var preview = new TextBox { Text = latest, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 320, MinWidth = 320 }; AutomationProperties.SetAutomationId(preview, "SideTransferPreview");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Append context to parent", Content = preview, PrimaryButtonText = "Transfer", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; transferDialog = dialog;
        var accepted = await dialog.ShowAsync(); transferDialog = null;
        if (accepted != ContentDialogResult.Primary || disposed || !Available() || string.IsNullOrWhiteSpace(preview.Text)) return;
        workspace.QueueSideTransfer(parent, preview.Text); transferQueued(); status.Text = "Transfer accepted · appended when the parent's current run finishes.";
    }
    void ClearPreview() { if (transferDialog?.Content is TextBox preview) preview.Text = ""; transferDialog?.Hide(); transferDialog = null; }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); ClearPreview(); response = ""; input.Text = ""; transcript.Children.Clear(); worker.Dispose(); }
}
