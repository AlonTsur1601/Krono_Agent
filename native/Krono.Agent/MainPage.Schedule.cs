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
        send.IsEnabled = false; stop.IsEnabled = true;
        if (scheduleStopButton is not null) scheduleStopButton.IsEnabled = true;
        activityChatId = run.Chat; RunActivity.Title = "Scheduled task running"; RunActivity.Message = task.Name; RunActivity.IsClosable = false; UpdateActivityVisibility();
        var answer = new System.Text.StringBuilder(); var credential = "";
        try {
            if (!ProviderCatalog.Ids.Contains(task.Provider)) throw new InvalidOperationException("This task's provider is no longer available.");
            var compatible = ProviderCatalog.Compatible(task.Provider);
            var endpoint = workspace.Setting("native.endpoint." + task.Provider) is { Length: > 0 } custom ? custom : ProviderCatalog.Endpoint(task.Provider);
            var address = compatible ? ProviderCatalog.ParseEndpoint(endpoint) : null;
            credential = workspace.Key(task.Provider);
            if (credential.Length == 0 && (ProviderCatalog.RequiresKey(task.Provider) || address is not null && !ProviderCatalog.Local(address))) throw new InvalidOperationException("Save the task provider's API key in Settings.");
            var toolsEnabled = !compatible || workspace.Setting("native.tools." + task.Provider + "." + task.Model) == "true";
            var dirs = task.Project is null ? [] : workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", task.Project).Select(row => row["path"]!.ToString()).ToArray();
            var memories = workspace.Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", task.Project).Select(row => row["content"]!.ToString()).ToArray();
            var instructions = task.Project is null ? "" : workspace.ProjectPreferences(task.Project)["instructions"]?.ToString() ?? "";
            scheduledWorker = new AgentWorker();
            scheduledWorker.Event += (id, type, data) => {
                if (generation != sessionGeneration || scheduledRun?.Id != id) return;
                if (type == "approval" && data is not null) DisplayApproval(data);
                if (type == "chunk" && data?["type"]?.ToString() == "text") answer.Append(data["content"]?.ToString());
                if (type == "chunk" && data?["type"]?.ToString() == "tool_call") RunActivity.Title = "Scheduled task using " + data["toolCall"]?["name"];
            };
            await scheduledWorker.Request(run.Id, "run", new { providerId = task.Provider, apiKey = credential, endpoint, toolsEnabled, modelId = task.Model,
                mode = task.Mode, permissions = task.Permissions, messages = new[] { new { role = "user", content = task.Prompt } }, projectDirs = dirs, instructions, memories });
            if (generation != sessionGeneration) return;
            if (scheduleStopped) { workspace.FinishScheduledRun(run.Id, "cancelled"); RunActivity.Title = "Scheduled task stopped"; }
            else { workspace.AddMessage(run.Chat, "assistant", answer.ToString()); workspace.FinishScheduledRun(run.Id, "completed"); RunActivity.Title = "Scheduled result ready"; }
        } catch (Exception error) {
            if (generation != sessionGeneration) return;
            var message = credential.Length > 0 ? error.Message.Replace(credential, "[redacted]", StringComparison.Ordinal) : error.Message;
            workspace.FinishScheduledRun(run.Id, scheduleStopped ? "cancelled" : "failed", scheduleStopped ? null : message[..Math.Min(1000, message.Length)]);
            RunActivity.Title = scheduleStopped ? "Scheduled task stopped" : "Scheduled task failed";
        } finally {
            if (generation == sessionGeneration) {
                scheduledWorker?.Dispose(); scheduledWorker = null; scheduledRun = null;
                send.IsEnabled = true; stop.IsEnabled = false; approvalId = null; Approval.IsOpen = false; ClearApprovalDetails();
                RunActivity.IsClosable = true; UpdateActivityVisibility();
                if (ReferenceEquals(Body.Content, chatView)) { ReloadChats(); if (chatId == run.Chat) LoadChat(); }
                if (scheduleStopButton is not null) scheduleStopButton.IsEnabled = false;
                if (ScheduleShowing) RefreshScheduleHistory();
            }
        }
    }
    void ShowSchedules(string? selectedId = null)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading("Scheduled tasks"));
        panel.Children.Add(new TextBlock { Text = "Tasks run on this Windows device while Krono is open and signed in. A missed schedule runs once when the app next becomes available.", TextWrapping = TextWrapping.Wrap, Opacity = .72 });
        var enabled = new ToggleSwitch { Header = "Run scheduled tasks on this device", IsOn = workspace.ScheduleEnabled }; AutomationProperties.SetAutomationId(enabled, "ScheduleEnabled");
        enabled.Toggled += (_, _) => workspace.SetSetting("native.scheduleEnabled", enabled.IsOn ? "true" : "false"); panel.Children.Add(enabled);
        var tasks = workspace.ScheduledTasks();
        var selection = new ComboBox { Header = "Edit a task", ItemsSource = new[] { "New task" }.Concat(tasks.Select(task => $"{task.Name} · {task.State} · {When(task)}")).ToArray(), SelectedIndex = 0, MinWidth = 280 };
        AutomationProperties.SetAutomationId(selection, "ScheduleSelector"); panel.Children.Add(selection);
        Workspace.ScheduledTask? selected = null;
        var title = Field("Task name", "ScheduleName", 100); var prompt = Field("Prompt", "SchedulePrompt", 32000); prompt.AcceptsReturn = true; prompt.MinHeight = 72; prompt.TextWrapping = TextWrapping.Wrap;
        var first = Field("First run (yyyy-MM-dd HH:mm)", "ScheduleFirstRun", 16);
        var interval = new NumberBox { Header = "Repeat every (minutes)", Minimum = 1, Maximum = 10080, Value = 5, MinWidth = 180 };
        var zones = TimeZoneInfo.GetSystemTimeZones().ToArray();
        var zone = new ComboBox { Header = "Time zone", ItemsSource = zones.Select(t => t.DisplayName).ToArray(), MinWidth = 260 };
        var providerIds = ProviderCatalog.Ids; var providerChoice = new ComboBox { Header = "Provider", ItemsSource = providerIds.Select(ProviderCatalog.Name).ToArray(), MinWidth = 180 };
        AutomationProperties.SetAutomationId(providerChoice, "ScheduleProvider");
        var modelField = Field("Model ID", "ScheduleModel", 400);
        var repeat = Choices("Repeat", ["once","interval","daily"], "once", value => interval.Visibility = value == "interval" ? Visibility.Visible : Visibility.Collapsed);
        var jobMode = Choices("Task mode", ["chat","plan","agent"], "chat", _ => { });
        var jobPermissions = Choices("Task permissions", ["ask","project","full"], "ask", _ => { });
        var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name");
        var projectChoice = new ComboBox { Header = "Project", ItemsSource = new[] { "No project" }.Concat(projects.Select(p => p["name"]!.ToString())).ToArray(), SelectedIndex = 0, MinWidth = 180 };
        providerChoice.SelectionChanged += (_, _) => { if (providerChoice.SelectedIndex >= 0) modelField.Text = workspace.Setting("native.model." + providerIds[providerChoice.SelectedIndex]); };
        void Load() {
            selected = selection.SelectedIndex > 0 ? tasks[selection.SelectedIndex - 1] : null;
            title.Text = selected?.Name ?? ""; prompt.Text = selected?.Prompt ?? "";
            var zoneId = selected?.TimeZone ?? TimeZoneInfo.Local.Id; zone.SelectedIndex = Math.Max(0, Array.FindIndex(zones, t => t.Id == zoneId));
            var clock = selected is null ? DateTimeOffset.Now.AddMinutes(5) : TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(selected.NextRun), zones[zone.SelectedIndex]); first.Text = clock.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            providerChoice.SelectedIndex = Math.Max(0, Array.IndexOf(providerIds, selected?.Provider ?? provider));
            modelField.Text = selected?.Model ?? model; repeat.SelectedItem = selected?.Recurrence ?? "once"; interval.Value = selected?.IntervalMinutes ?? 5;
            interval.Visibility = repeat.SelectedItem?.ToString() == "interval" ? Visibility.Visible : Visibility.Collapsed;
            jobMode.SelectedItem = selected?.Mode ?? "chat"; jobPermissions.SelectedItem = selected?.Permissions ?? "ask";
            projectChoice.SelectedIndex = selected?.Project is { } id ? projects.FindIndex(p => p["id"]?.ToString() == id) + 1 : 0;
        }
        selection.SelectionChanged += (_, _) => Load();
        if (selectedId is not null) selection.SelectedIndex = tasks.FindIndex(t => t.Id == selectedId) + 1;
        Load();
        foreach (var item in new Control[] { title,prompt,providerChoice,modelField,projectChoice,jobMode,jobPermissions,zone,first,repeat,interval }) panel.Children.Add(item);
        panel.Children.Add(new TextBlock { Text = "Chat has no tools. Ask waits for approval if the task requests a change; Full can run tools with your Windows permissions.", TextWrapping = TextWrapping.Wrap, Opacity = .72 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Save schedule", async () => {
            if (!DateTime.TryParseExact(first.Text.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) throw new InvalidOperationException("Enter a first run as yyyy-MM-dd HH:mm.");
            if (providerChoice.SelectedIndex < 0 || zone.SelectedIndex < 0) throw new InvalidOperationException("Choose a provider and time zone.");
            var selectedMode = jobMode.SelectedItem!.ToString()!; var permission = jobPermissions.SelectedItem!.ToString()!;
            if (selectedMode == "agent" && permission == "full" && !await Confirm("Allow unattended tools?", "This scheduled task may change files and run commands with your Windows permissions.")) return;
            var taskId = selected?.Id ?? Guid.NewGuid().ToString("N"); var zoneValue = zones[zone.SelectedIndex];
            workspace.SaveTask(new(taskId, title.Text, prompt.Text, providerIds[providerChoice.SelectedIndex], modelField.Text, selectedMode, permission,
                projectChoice.SelectedIndex > 0 ? projects[projectChoice.SelectedIndex - 1]["id"]!.ToString() : null, repeat.SelectedItem!.ToString()!, zoneValue.Id,
                double.IsFinite(interval.Value) ? (int)interval.Value : 5, local.Hour*60+local.Minute, Workspace.ScheduleTime(local, zoneValue).ToUnixTimeMilliseconds(), selected?.State == "paused" ? "paused" : "active"));
            ShowSchedules(taskId); await CheckSchedules();
        }));
        actions.Children.Add(Button("Pause / resume task", () => { if (selected is { } task) { workspace.SetTaskState(task.Id, task.State == "paused" ? "active" : "paused"); ShowSchedules(task.Id); } }));
        actions.Children.Add(Button("Cancel task", () => { if (selected is { } task) { workspace.SetTaskState(task.Id, "cancelled"); ShowSchedules(); } }));
        scheduleStopButton = Button("Stop active run", StopScheduledRun); scheduleStopButton.IsEnabled = scheduledRun is not null; actions.Children.Add(scheduleStopButton);
        panel.Children.Add(actions); panel.Children.Add(Heading("Run history"));
        scheduleHistoryPane = new StackPanel { Spacing = 8 }; panel.Children.Add(scheduleHistoryPane); RefreshScheduleHistory();
        KronoDesign.Present(Body, new ScrollViewer { Content = panel }); UpdateActivityVisibility();
        static TextBox Field(string header, string id, int limit) { var box = new TextBox { Header = header, MaxLength = limit }; AutomationProperties.SetAutomationId(box, id); return box; }
        static string When(Workspace.ScheduledTask task) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(task.NextRun), TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone)).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
    }
    void RefreshScheduleHistory()
    {
        if (scheduleHistoryPane is not { } panel) return;
        panel.Children.Clear();
        var history = workspace.ScheduledHistory();
        if (history.Count == 0) panel.Children.Add(new TextBlock { Text = "No scheduled runs yet.", Opacity = .65 });
        foreach (var run in history) {
            var state = run["state"]!.ToString(); var name = run["name"]!.ToString();
            panel.Children.Add(new TextBlock { Text = $"{name} · {state} · {DateTimeOffset.FromUnixTimeMilliseconds(run["started_at"]!.GetValue<long>()).ToLocalTime():g}", TextWrapping = TextWrapping.Wrap });
            if (run["error"] is { } failure) panel.Children.Add(new TextBlock { Text = failure.ToString(), TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            if (run["chat_id"] is { } id) panel.Children.Add(Button("Open run conversation", () => OpenNormalChat(id.ToString())));
        }
    }
}
