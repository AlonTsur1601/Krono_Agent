using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Krono_Agent;

public sealed partial class MainPage
{
    sealed record RunFileChange(string Path, string Before, string After);
    sealed record RunSummary(string[] Actions, RunFileChange[] Changes);
    readonly Dictionary<string, RunFileChange> runChanges = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> runActions = [];
    readonly StackPanel conversationMap = new() { Spacing = 3, Margin = new Thickness(4, 0, 1, 0) };
    readonly DispatcherTimer streamTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly System.Text.StringBuilder responseBuffer = new();
    string? splitWorkspace;
    StackPanel? fileChanges;
    TextBlock? fileText;
    bool streamDirty;

    void InitializeChatWorkspace()
    {
        streamTimer.Tick += (_, _) => FlushStream();
        AutomationProperties.SetName(conversationMap, "Conversation map");
    }

    void FlushStream()
    {
        if (!streamDirty || streaming is null) return;
        streamDirty = false;
        var nearEnd = transcript.ScrollableHeight - transcript.VerticalOffset < 90;
        streaming.Text = responseBuffer.ToString();
        if (nearEnd) transcript.ChangeView(null, transcript.ScrollableHeight, null, true);
    }

    void BeginRunSummary()
    {
        runActions.Clear();
        runChanges.Clear();
        RefreshRunFiles();
    }

    void SaveRunSummary(string messageId)
    {
        if (runActions.Count == 0 && runChanges.Count == 0) return;
        workspace.SetSetting("native.runSummary." + messageId, JsonSerializer.Serialize(new RunSummary(runActions.ToArray(), runChanges.Values.ToArray())));
    }

    void ConfigureWorkspaceWorker(AgentWorker instance)
    {
        instance.BrowserRequest = async (_, request) => {
            browser ??= new BrowserWorkspace(workspace, this);
            if (!ReferenceEquals(Body.Content, browser) && !ReferenceEquals(SideBody.Content, browser)) OpenWorkspace("Browser");
            return await browser.RunAgentBrowser(request);
        };
    }

    void ReceiveAgentWorkspace(string id, string type, JsonNode? data)
    {
        if (type == "terminal-agent" && terminal is null && worker is not null) terminal = new TerminalWorkspace(worker);
        terminal?.Receive(id, type, data);
        if (type == "chunk" && data?["type"]?.ToString() == "tool_call" && data["toolCall"]?["name"]?.ToString() is { } name) {
            if (runActions.Count < 100) runActions.Add(name.Replace('_', ' '));
        }
        if (type != "file-change" || data?["path"]?.ToString() is not { Length: > 0 } path) return;
        if (!runChanges.TryGetValue(path, out var prior) && runChanges.Count >= 16) return;
        runChanges[path] = new(path, prior?.Before ?? data["before"]?.ToString() ?? "", data["after"]?.ToString() ?? "");
        RefreshRunFiles();
    }

    void OpenWorkspace(string kind)
    {
        if (kind == splitWorkspace && SideSurface.Visibility == Visibility.Visible) return;
        splitWorkspace = kind;
        FrameworkElement content;
        switch (kind) {
            case "Browser": content = browser ??= new BrowserWorkspace(workspace, this); break;
            case "Terminal":
                if (worker is null) return;
                terminal ??= new TerminalWorkspace(worker);
                if ((runId ?? scheduledRun?.Id) is { } active) terminal.SelectAgentRun(active);
                content = terminal;
                break;
            default: content = BuildFileWorkspace(); break;
        }

        if (ReferenceEquals(Body.Content, content)) {
            Body.Content = null;
            ShowChats();
        }
        var host = new Grid { RowSpacing = 8 };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition());
        var toolbar = new Grid { ColumnSpacing = 5 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(new TextBlock { Text = kind, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = Button("Close", () => {
            splitWorkspace = null;
            SideBody.Content = null;
            if (sideChat is not null && sideChat.ParentChatId == chatId) KronoDesign.Present(SideBody, sideChat);
            SetSideChatVisible(SideBody.Content is not null);
        });
        Grid.SetColumn(close, 1);
        toolbar.Children.Add(close);
        host.Children.Add(toolbar);
        if (content.Parent is Panel parent) parent.Children.Remove(content);
        Grid.SetRow(content, 1);
        host.Children.Add(content);
        KronoDesign.Present(SideBody, host);
        SetSideChatVisible(true);
    }

    void DetachWorkspace(FrameworkElement content)
    {
        if (content.Parent is not Panel parent || !ReferenceEquals(SideBody.Content, parent)) return;
        parent.Children.Remove(content);
        SideBody.Content = null;
        splitWorkspace = null;
        SetSideChatVisible(false);
    }

    FrameworkElement BuildFileWorkspace()
    {
        fileChanges = new StackPanel { Spacing = 5 };
        fileText = new TextBlock { FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        var browser = new StackPanel { Spacing = 6, MaxHeight = 205 };
        browser.Children.Add(fileChanges);
        if (chatId is { } id) {
            var tree = new TreeView { SelectionMode = TreeViewSelectionMode.None, MaxHeight = 175 };
            AutomationProperties.SetName(tree, "Project files");
            foreach (var row in workspace.Query("SELECT path FROM project_directories WHERE project_id=(SELECT project_id FROM chats WHERE id=$p0)", id)) {
                if (row["path"]?.ToString() is not { } path || !Directory.Exists(path)) continue;
                tree.RootNodes.Add(new TreeViewNode { Content = path, HasUnrealizedChildren = true });
            }
            tree.Expanding += async (_, args) => {
                if (!args.Node.HasUnrealizedChildren || args.Node.Content is not string path) return;
                args.Node.HasUnrealizedChildren = false;
                var entries = await Task.Run(() => {
                    try {
                        return new DirectoryInfo(path).EnumerateFileSystemInfos().Where(item => (item.Attributes & FileAttributes.ReparsePoint) == 0 &&
                            item.Name is not (".git" or "node_modules" or "bin" or "obj")).Take(200)
                            .OrderBy(item => item is not DirectoryInfo).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(item => (item.FullName, Folder: item is DirectoryInfo)).ToArray();
                    } catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; }
                });
                foreach (var entry in entries) args.Node.Children.Add(new TreeViewNode { Content = entry.FullName, HasUnrealizedChildren = entry.Folder });
            };
            tree.ItemInvoked += async (_, args) => {
                var item = args.InvokedItem is TreeViewNode node ? node.Content : args.InvokedItem;
                if (item is not string path || !File.Exists(path) || fileText is not { } preview) return;
                if (runChanges.TryGetValue(path, out var change)) { ShowChange(change); return; }
                var name = Path.GetFileName(path);
                if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key") || name.EndsWith(".pem") || name == ".npmrc") {
                    preview.Text = "This file can contain credentials.";
                    return;
                }
                try {
                    using var reader = File.OpenText(path);
                    var chars = new char[16_384];
                    var count = await reader.ReadBlockAsync(chars);
                    var value = new string(chars, 0, count);
                    preview.Text = value.Contains('\0') ? "Preview unavailable for binary files." : value;
                } catch (Exception error) { preview.Text = FriendlyError(error); }
            };
            if (tree.RootNodes.Count > 0) browser.Children.Add(tree);
        }
        layout.Children.Add(new ScrollViewer { Content = browser, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var previewPane = new ScrollViewer { Content = fileText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(previewPane, 1);
        layout.Children.Add(previewPane);
        RefreshRunFiles();
        return layout;
    }

    void RefreshRunFiles()
    {
        if (fileChanges is not { } changes) return;
        changes.Children.Clear();
        foreach (var change in runChanges.Values) {
            var entry = Button(Path.GetFileName(change.Path), () => ShowChange(change));
            entry.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTipService.SetToolTip(entry, change.Path);
            changes.Children.Add(entry);
        }
    }

    void ShowChange(RunFileChange change)
    {
        if (fileText is null) return;
        var before = change.Before.ReplaceLineEndings("\n").Split('\n');
        var after = change.After.ReplaceLineEndings("\n").Split('\n');
        var start = 0;
        while (start < Math.Min(before.Length, after.Length) && before[start] == after[start]) start++;
        var end = 0;
        while (end < Math.Min(before.Length, after.Length) - start && before[^(end + 1)] == after[^(end + 1)]) end++;
        var context = before.Skip(Math.Max(0, start - 2)).Take(Math.Min(start, 2)).Select(line => "  " + line);
        var removed = before.Skip(start).Take(before.Length - start - end).Where(line => change.Before.Length > 0).Select(line => "− " + line);
        var added = after.Skip(start).Take(after.Length - start - end).Where(line => change.After.Length > 0).Select(line => "+ " + line);
        fileText.Text = change.Path + "\n\n" + (start == before.Length && start == after.Length ? "No text difference available." : string.Join('\n', context.Concat(removed).Concat(added)));
    }

    void AddResponseActions(StackPanel box, string text, string messageId)
    {
        if (workspace.Setting("native.runSummary." + messageId) is { Length: > 0 } saved &&
            JsonSerializer.Deserialize<RunSummary>(saved) is { } summary) {
            if (summary.Actions.Length > 0) box.Children.Add(KronoDesign.Section("Actions · " + summary.Actions.Length,
                new TextBlock { Text = string.Join("\n", summary.Actions.Select(name => "• " + name)), TextWrapping = TextWrapping.Wrap }));
            if (summary.Changes.Length > 0) {
                var details = new StackPanel { Spacing = 5 };
                details.Children.Add(new TextBlock { Text = string.Join("\n", summary.Changes.Select(change => Path.GetFileName(change.Path))), TextWrapping = TextWrapping.Wrap });
                details.Children.Add(Button("Review changes", () => {
                    if (runId is null && scheduledRun is null) {
                        runChanges.Clear();
                        foreach (var change in summary.Changes) runChanges[change.Path] = change;
                    }
                    OpenWorkspace("Files");
                    RefreshRunFiles();
                    ShowChange(summary.Changes[0]);
                }));
                box.Children.Add(KronoDesign.Section($"Changed files · {summary.Changes.Length}", details));
            }
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(HistoryAction("Copy response", new SymbolIcon(Symbol.Copy), () => {
            var clipboard = new DataPackage();
            clipboard.SetText(text);
            Clipboard.SetContent(clipboard);
        }));
        actions.Children.Add(HistoryAction("Branch conversation", KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Folder), () => OpenSideChatFrom(messageId)));
        box.Children.Add(actions);
        box.Children.Add(new TextBlock { Text = "Krono can make mistakes. Verify important details.", FontSize = 11, Opacity = .58, TextWrapping = TextWrapping.Wrap });
    }

    void AddMapMark(FrameworkElement bubble, string text, bool user)
    {
        var preview = text.ReplaceLineEndings(" ").Trim();
        if (preview.Length > 110) preview = preview[..109] + "…";
        var mark = new Button { Width = 20, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            CornerRadius = new CornerRadius(5), Template = (ControlTemplate)Application.Current.Resources["KronoQuietIconTemplate"],
            Content = new Border { Width = user ? 13 : 8, Height = 3, CornerRadius = new CornerRadius(2),
                Background = KronoDesign.ThemeBrush(user ? "KronoAccentForeground" : "KronoMuted", ActualTheme) } };
        AutomationProperties.SetName(mark, preview.Length > 0 ? preview : "Message");
        ToolTipService.SetToolTip(mark, preview);
        mark.Click += (_, _) => bubble.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0 });
        conversationMap.Children.Add(mark);
    }
}
