using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly DispatcherTimer scheduleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    Workspace.ScheduledRun? scheduledRun;
    AgentWorker? scheduledWorker;
    bool scheduleStopped;
    StackPanel? scheduleHistoryPane;
    Button? scheduleStopButton;
    void InitializeSchedule() => scheduleTimer.Tick += async (_, _) => await CheckSchedules();
    bool ScheduleShowing => Nav.SelectedItem is NavigationViewItem item && item.Tag?.ToString() == "schedule";
    void StopSchedules()
    {
        scheduleTimer.Stop();
        if (scheduledRun is not null && workspace.AccountId.Length > 0) workspace.FinishScheduledRun(scheduledRun.Id, "interrupted", "The app was closed or signed out.");
        scheduledRun = null; scheduledWorker?.Dispose(); scheduledWorker = null; scheduleHistoryPane = null; scheduleStopButton = null;
    }
    async Task StopScheduledRun()
    {
        if (scheduledRun is not { } run || scheduledWorker is not { } active) return;
        scheduleStopped = true; await active.Send(Guid.NewGuid().ToString(), "cancel", new { id = run.Id });
    }
    async Task CheckSchedules()
    {
        if (workspace.AccountId.Length == 0) return;
        try {
            if (scheduledRun is not null) { workspace.PulseScheduledRun(scheduledRun.Id); return; }
            if (runId is not null || !workspace.ScheduleEnabled) return;
            var run = workspace.ClaimScheduledRun(Workspace.Now); if (run is not null) await ExecuteSchedule(run);
        } catch (Exception error) { Error(error); }
    }
    async Task ExecuteSchedule(Workspace.ScheduledRun run)
    {
        var generation = sessionGeneration; var task = run.Task; scheduledRun = run; scheduleStopped = false;
        BeginRunSummary();
        RefreshComposerAction();
        if (scheduleStopButton is not null) scheduleStopButton.IsEnabled = true;
        activityChatId = run.Chat; RunActivity.Title = "Scheduled task running"; RunActivity.Message = task.Name; RunActivity.IsClosable = false; UpdateActivityVisibility();
        ReloadChats();
        var answer = new System.Text.StringBuilder(); var credential = "";
        try {
            if (!ProviderCatalog.Ids.Contains(task.Provider)) throw new InvalidOperationException("This task's provider is no longer available.");
            var compatible = ProviderCatalog.Compatible(task.Provider);
            var endpoint = workspace.Setting("native.endpoint." + task.Provider) is { Length: > 0 } custom ? custom : ProviderCatalog.Endpoint(task.Provider);
            var automatic = ProviderCatalog.AutomaticLocal(task.Provider);
            var address = compatible && !automatic ? ProviderCatalog.ParseEndpoint(endpoint) : null;
            credential = automatic ? "" : workspace.Key(task.Provider);
            if (credential.Length == 0 && (ProviderCatalog.RequiresKey(task.Provider) || address is not null && !ProviderCatalog.Local(address))) throw new InvalidOperationException("Save the task provider's API key in Settings.");
            var toolsEnabled = !compatible || workspace.Setting("native.tools." + task.Provider + "." + task.Model) == "true";
            var dirs = task.Project is null ? [] : workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", task.Project).Select(row => row["path"]!.ToString()).ToArray();
            var memories = workspace.Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", task.Project).Select(row => row["content"]!.ToString()).ToArray();
            var instructions = workspace.Instructions(task.Project);
            scheduledWorker = new AgentWorker();
            scheduledWorker.Event += (id, type, data) => {
                if (generation != sessionGeneration || scheduledRun?.Id != id) return;
                ReceiveAgentWorkspace(id, type, data);
                if (type == "approval" && data is not null) DisplayApproval(data);
                if (type == "chunk" && data?["type"]?.ToString() == "text") answer.Append(data["content"]?.ToString());
                if (type == "chunk" && data?["type"]?.ToString() == "tool_call") RunActivity.Title = "Scheduled task using " + data["toolCall"]?["name"];
            };
            var options = task.Chat is null ? null : workspace.GetChatOptions(run.Chat);
            var history = workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at,rowid", run.Chat)
                .Select(row => new { role = row["role"]!.ToString(), content = row["content"]?.ToString() ?? "" }).ToArray();
            if (options?.Context == "Recent messages") history = history.TakeLast(20).ToArray();
            var effort = IsReasoningEffort(options?.Reasoning) ? options?.Reasoning : null;
            ConfigureWorkspaceWorker(scheduledWorker);
            var request = System.Text.Json.JsonSerializer.SerializeToNode(new { providerId = task.Provider, apiKey = credential, endpoint, toolsEnabled, modelId = task.Model,
                mode = task.Mode, permissions = task.Permissions, messages = history, projectDirs = dirs, instructions, memories, fastMode = options?.FastMode == true,
                plugins = (task.Mode is "agent" or "goal") && toolsEnabled ? PluginCatalog.Enabled(workspace) : [] })!.AsObject();
            if (effort is not null) request["reasoningEffort"] = effort;
            await scheduledWorker.Request(run.Id, "run", request);
            if (generation != sessionGeneration) return;
            if (scheduleStopped) { workspace.FinishScheduledRun(run.Id, "cancelled"); RunActivity.Title = "Scheduled task stopped"; }
            else { SaveRunSummary(workspace.AddMessage(run.Chat, "assistant", answer.ToString())); workspace.FinishScheduledRun(run.Id, "completed"); RunActivity.Title = "Scheduled result ready"; }
        } catch (Exception error) {
            if (generation != sessionGeneration) return;
            var message = FriendlyError(error);
            if (credential.Length > 0) message = message.Replace(credential, "[redacted]", StringComparison.Ordinal);
            workspace.FinishScheduledRun(run.Id, scheduleStopped ? "cancelled" : "failed", scheduleStopped ? null : message[..Math.Min(1000, message.Length)]);
            RunActivity.Title = scheduleStopped ? "Scheduled task stopped" : "Scheduled task failed";
        } finally {
            if (generation == sessionGeneration) {
                scheduledWorker?.Dispose(); scheduledWorker = null; scheduledRun = null;
                RefreshComposerAction(); approvalId = null; Approval.IsOpen = false; ClearApprovalDetails();
                RunActivity.IsClosable = true; UpdateActivityVisibility();
                if (ReferenceEquals(Body.Content, chatView)) { ReloadChats(); if (chatId == run.Chat) LoadChat(); }
                if (scheduleStopButton is not null) scheduleStopButton.IsEnabled = false;
                if (ScheduleShowing) RefreshScheduleHistory();
            }
        }
    }
    const string ScheduleDateFormat = "yyyy-MM-dd, HH:mm";
    static string ScheduleState(string state) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(state);

    void ShowSchedules(string? selectedId = null, bool editing = false)
    {
        var tasks = workspace.ScheduledTasks();
        var selected = tasks.FirstOrDefault(task => task.Id == selectedId);
        var sidebar = new StackPanel { Spacing = 8 };
        sidebar.Children.Add(Heading("Scheduled tasks"));
        sidebar.Children.Add(Button("+ New task", () => ShowSchedules(editing: true)));
        foreach (var task in tasks) {
            var item = Button(task.Name, () => ShowSchedules(task.Id));
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "Edit task" };
            edit.Click += (_, _) => ShowSchedules(task.Id, true);
            menu.Items.Add(edit);
            item.ContextFlyout = menu;
            sidebar.Children.Add(item);
        }
        ShowContext(sidebar);

        var panel = new StackPanel { Spacing = 16, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading(editing ? selected?.Name ?? "New scheduled task" : selected?.Name ?? "Scheduled tasks"));
        if (!editing) {
            if (selected is { } task) {
                var recurrence = task.Recurrence switch {
                    "daily" => "Daily", "interval" => $"Every {task.IntervalMinutes} minutes", _ => "Once"
                };
                if (task.Recurrence != "once" && task.RunLimit is { } maximum) recurrence += $" · Up to {maximum} runs";
                var next = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(task.NextRun),
                    TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone));
                var timing = task.State is "active" or "paused" ? $"  /  Next run {next.ToString(ScheduleDateFormat, CultureInfo.InvariantCulture)}" : "";
                panel.Children.Add(new TextBlock { Text = $"{ScheduleState(task.State)}  /  {recurrence}{timing}", TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = ProviderCatalog.Name(task.Provider) + " / " + task.Model, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = task.Prompt, TextWrapping = TextWrapping.Wrap });
                var taskActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                taskActions.Children.Add(Button("Edit task", () => ShowSchedules(task.Id, true)));
                if (task.State is "active" or "paused") taskActions.Children.Add(Button(task.State == "paused" ? "Resume task" : "Pause task", () => {
                    workspace.SetTaskState(task.Id, task.State == "paused" ? "active" : "paused"); ShowSchedules(task.Id);
                }));
                if (task.State is "active" or "paused") taskActions.Children.Add(Button("Cancel task", async () => {
                    if (!await Confirm("Cancel scheduled task?", task.Name + " will stop running on its schedule.", "Cancel task")) return;
                    workspace.SetTaskState(task.Id, "cancelled");
                    ShowSchedules();
                }));
                panel.Children.Add(taskActions);
            } else {
                panel.Children.Add(new TextBlock { Text = "Choose a task to view it, or create a new one. Tasks run while Krono is open and signed in.", TextWrapping = TextWrapping.Wrap });
            }
            if (selected is null || !workspace.ScheduleEnabled) {
                var enabled = KronoDesign.Toggle("Run scheduled tasks on this device", workspace.ScheduleEnabled);
                AutomationProperties.SetAutomationId(enabled, "ScheduleEnabled");
                enabled.Toggled += (_, _) => workspace.SetSetting("native.scheduleEnabled", enabled.IsOn ? "true" : "false");
                panel.Children.Add(enabled);
            }
            scheduleStopButton = Button("Stop active run", StopScheduledRun);
            scheduleStopButton.IsEnabled = scheduledRun is not null;
            panel.Children.Add(scheduleStopButton);
            scheduleHistoryPane = new StackPanel { Spacing = 8 };
            panel.Children.Add(Heading("Run history"));
            AutomationProperties.SetAutomationId(scheduleHistoryPane, "ScheduleHistory");
            panel.Children.Add(scheduleHistoryPane);
            RefreshScheduleHistory();
            KronoDesign.Present(Body, KronoDesign.ScrollPage(panel));
            UpdateActivityVisibility();
            return;
        }

        var title = Field("Task name", "ScheduleName", 100); var prompt = Field("Prompt", "SchedulePrompt", 32000); prompt.AcceptsReturn = true; prompt.MinHeight = 72; prompt.TextWrapping = TextWrapping.Wrap;
        var first = Field("First run (year-month-day, hour:minute)", "ScheduleFirstRun", 17);
        var interval = new NumberBox { Header = "Repeat every (minutes)", Minimum = 1, Maximum = 10080, Value = 5, MinWidth = 180 };
        var zones = TimeZoneInfo.GetSystemTimeZones().ToArray();
        var zone = new KronoComboBox { Header = "Time zone", ItemsSource = zones.Select(t => {
            var offset = t.BaseUtcOffset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"{t.Id} (UTC{sign}{offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)})";
        }).ToArray(), MinWidth = 260 };
        var providerIds = ProviderCatalog.Ids; var providerChoice = new KronoComboBox { Header = "Provider", ItemsSource = providerIds.Select(ProviderCatalog.Name).ToArray(), MinWidth = 180 };
        AutomationProperties.SetAutomationId(providerChoice, "ScheduleProvider");
        var modelField = new KronoComboBox { Header = "Model", PlaceholderText = "Choose an available model", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(modelField, "ScheduleModel");
        var limit = new NumberBox { Header = "Total runs (0 means no limit)", Minimum = 0, Maximum = 10000, Value = 0 };
        KronoInputMotion.Attach(interval);
        KronoInputMotion.Attach(limit);
        void ShowRecurrence(string value)
        {
            interval.Visibility = value == "interval" ? Visibility.Visible : Visibility.Collapsed;
            limit.Visibility = value == "once" ? Visibility.Collapsed : Visibility.Visible;
        }
        var repeat = Choices("Repeat", ["once","interval","daily"], "once", ShowRecurrence);
        var jobMode = Choices("Task mode", ["chat","plan","agent","goal"], "agent", _ => { });
        var jobPermissions = Choices("Task permissions", ["ask","project","full"], "ask", _ => { });
        jobPermissions.SelectionChanged += (_, _) => MarkFullAccess(jobPermissions, jobPermissions.SelectedItem?.ToString() ?? "ask");
        var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name");
        var projectChoice = new KronoComboBox { Header = "Project", ItemsSource = new[] { "No project" }.Concat(projects.Select(p => p["name"]!.ToString())).ToArray(), SelectedIndex = 0, MinWidth = 180 };
        var chats = workspace.Query("SELECT id,name,project_id FROM chats WHERE account_id=$p0 AND deleted_at IS NULL AND id NOT IN (SELECT chat_id FROM side_chats) ORDER BY updated_at DESC", workspace.AccountId);
        var target = new KronoComboBox { Header = "Run in", ItemsSource = new[] { "New conversation", "Existing conversation" },
            SelectedIndex = selected?.Chat is null ? 0 : 1 };
        var conversation = new KronoComboBox { Header = "Conversation", ItemsSource = chats.Select(row => row["name"]!.ToString()).ToArray(),
            SelectedIndex = chats.FindIndex(row => row["id"]?.ToString() == selected?.Chat), HorizontalAlignment = HorizontalAlignment.Stretch };
        void Load() {
            title.Text = selected?.Name ?? ""; prompt.Text = selected?.Prompt ?? "";
            var zoneId = selected?.TimeZone ?? TimeZoneInfo.Local.Id; zone.SelectedIndex = Math.Max(0, Array.FindIndex(zones, t => t.Id == zoneId));
            var clock = selected is null ? DateTimeOffset.Now.AddMinutes(5) : TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(selected.NextRun), zones[zone.SelectedIndex]); first.Text = clock.ToString(ScheduleDateFormat, CultureInfo.InvariantCulture);
            providerChoice.SelectedIndex = Math.Max(0, Array.IndexOf(providerIds, selected?.Provider ?? provider));
            repeat.SelectedItem = selected?.Recurrence ?? "once"; interval.Value = selected?.IntervalMinutes ?? 5;
            limit.Value = selected?.RunLimit ?? 0;
            ShowRecurrence(repeat.SelectedItem?.ToString() ?? "once");
            jobMode.SelectedItem = selected?.Mode ?? "agent"; jobPermissions.SelectedItem = selected?.Permissions ?? "ask";
            projectChoice.SelectedIndex = selected?.Project is { } id ? projects.FindIndex(p => p["id"]?.ToString() == id) + 1 : 0;
        }
        Load();
        panel.Children.Add(title);
        panel.Children.Add(prompt);
        panel.Children.Add(Heading("When to run"));
        panel.Children.Add(first);
        panel.Children.Add(repeat);
        panel.Children.Add(interval);
        panel.Children.Add(limit);
        panel.Children.Add(target);
        panel.Children.Add(conversation);
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(zone);
        advanced.Children.Add(projectChoice);
        advanced.Children.Add(jobMode);
        advanced.Children.Add(jobPermissions);
        var modelSettings = new StackPanel { Spacing = 8 };
        modelSettings.Children.Add(providerChoice);
        modelSettings.Children.Add(modelField);
        var modelOptions = KronoDesign.Section("Model", modelSettings);
        AutomationProperties.SetAutomationId(modelOptions, "ScheduleModelOptions");
        int modelRequest = 0;
        async Task RefreshAvailableModels(bool keep = false)
        {
            var request = ++modelRequest;
            if (providerChoice.SelectedIndex < 0 || worker is null) return;
            var id = providerIds[providerChoice.SelectedIndex];
            var preferred = keep ? modelField.SelectedItem as string ?? (selected?.Provider == id ? selected.Model : null) : null;
            preferred ??= workspace.Setting("native.model." + id);
            modelField.ItemsSource = ModelChoices(id, [], preferred);
            modelField.SelectedItem = preferred;
            try {
                var models = await DiscoverModels(id);
                if (request != modelRequest || !modelOptions.IsExpanded || !modelOptions.IsLoaded) return;
                modelField.ItemsSource = ModelChoices(id, models, preferred);
                modelField.SelectedItem = preferred;
            } catch (Exception error) { if (request == modelRequest && modelOptions.IsLoaded) Error(error); }
        }
        modelOptions.Expanding += async (_, _) => await RefreshAvailableModels(true);
        providerChoice.SelectionChanged += async (_, _) => await RefreshAvailableModels();
        panel.Children.Add(modelOptions);
        panel.Children.Add(KronoDesign.Section("More options", advanced));
        void RefreshTarget() {
            var existing = target.SelectedIndex == 1;
            conversation.Visibility = existing ? Visibility.Visible : Visibility.Collapsed;
            modelOptions.Visibility = projectChoice.Visibility = jobMode.Visibility = jobPermissions.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        }
        target.SelectionChanged += (_, _) => RefreshTarget();
        RefreshTarget();
        modelOptions.IsExpanded = true;
        bool saving = false;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        async Task SaveSchedule() {
            if (saving) return;
            saving = true;
            try {
            if (!DateTime.TryParseExact(first.Text.Trim(), [ScheduleDateFormat, "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) throw new InvalidOperationException("Enter a date and time, for example 2026-12-01, 09:30.");
            if (providerChoice.SelectedIndex < 0 || zone.SelectedIndex < 0) throw new InvalidOperationException("Choose a provider and time zone.");
            var selectedMode = jobMode.SelectedItem!.ToString()!; var permission = jobPermissions.SelectedItem!.ToString()!;
            var providerId = providerIds[providerChoice.SelectedIndex];
            var chosenModel = modelField.SelectedItem as string;
            var projectId = projectChoice.SelectedIndex > 0 ? projects[projectChoice.SelectedIndex - 1]["id"]!.ToString() : null;
            string? chat = null;
            if (target.SelectedIndex == 1) {
                if (conversation.SelectedIndex < 0) throw new InvalidOperationException("Choose an existing conversation.");
                var row = chats[conversation.SelectedIndex];
                chat = row["id"]!.ToString();
                var options = workspace.GetChatOptions(chat) ?? throw new InvalidOperationException("Send a message in this conversation first so Krono can save its run settings.");
                providerId = options.Provider; chosenModel = options.Model; selectedMode = options.Mode; permission = options.Permissions;
                projectId = row["project_id"]?.ToString();
            }
            if (chosenModel is null) throw new InvalidOperationException("Choose a model for the task.");
            if (selectedMode is "agent" or "goal" && permission == "full" && !await Confirm("Allow unattended tools?", "This scheduled task may change files and run commands with your Windows permissions.")) return;
            var taskId = selected?.Id ?? Guid.NewGuid().ToString("N"); var zoneValue = zones[zone.SelectedIndex];
            workspace.SaveTask(new(taskId, title.Text, prompt.Text, providerId, chosenModel, selectedMode, permission,
                projectId, repeat.SelectedItem!.ToString()!, zoneValue.Id,
                double.IsFinite(interval.Value) ? (int)interval.Value : 5, local.Hour*60+local.Minute, Workspace.ScheduleTime(local, zoneValue).ToUnixTimeMilliseconds(), selected?.State == "paused" ? "paused" : "active",
                repeat.SelectedItem?.ToString() != "once" && double.IsFinite(limit.Value) && limit.Value >= 1 ? (int)limit.Value : null, chat));
            ShowSchedules(taskId); await CheckSchedules();
            } finally { saving = false; }
        }
        actions.Children.Add(Button("Save schedule", SaveSchedule));
        first.PreviewKeyDown += async (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Enter || args.KeyStatus.WasKeyDown) return;
            args.Handled = true;
            try { await SaveSchedule(); } catch (Exception error) { Error(error); }
        };
        actions.Children.Add(Button("Cancel", () => ShowSchedules(selected?.Id)));
        panel.Children.Add(actions);
        KronoDesign.Present(Body, KronoDesign.ScrollPage(panel));
        UpdateActivityVisibility();
        static TextBox Field(string header, string id, int limit) { var box = new KronoTextBox { Header = header, MaxLength = limit }; AutomationProperties.SetAutomationId(box, id); return box; }
    }
    void RefreshScheduleHistory()
    {
        if (scheduleHistoryPane is not { } panel) return;
        panel.Children.Clear();
        var history = workspace.ScheduledHistory();
        if (history.Count == 0) panel.Children.Add(new TextBlock { Text = "No scheduled runs yet.", Opacity = .65 });
        foreach (var run in history) {
            var state = run["state"]!.ToString(); var name = run["name"]!.ToString();
            panel.Children.Add(new TextBlock { Text = $"{name} · {ScheduleState(state)} · {DateTimeOffset.FromUnixTimeMilliseconds(run["started_at"]!.GetValue<long>()).ToLocalTime().ToString(ScheduleDateFormat, CultureInfo.InvariantCulture)}", TextWrapping = TextWrapping.Wrap });
            if (run["error"] is { } failure) panel.Children.Add(new TextBlock { Text = failure.ToString(), TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            if (run["chat_id"] is { } id) panel.Children.Add(Button("Open run conversation", () => OpenNormalChat(id.ToString())));
        }
    }
}
