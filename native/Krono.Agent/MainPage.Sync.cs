using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly DispatcherTimer accountSyncTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    CancellationTokenSource? syncRun;
    TextBlock? syncStatus;
    Button? syncButton, syncReview;
    bool reviewingSync;
    string syncMessage = "";
    void InitializeSync() => accountSyncTimer.Tick += async (_, _) => await SyncAccount(false);
    void StopSync() { accountSyncTimer.Stop(); syncRun?.Cancel(); syncMessage = ""; syncStatus = null; syncButton = syncReview = null; }
    bool SafeToSync() => workspace.AccountId.Length > 0 && runId is null && scheduledRun is null && sideChat is null && !reviewingSync && (games is null || !ReferenceEquals(Body.Content, games));
    void AddSyncSettings(StackPanel panel)
    {
        panel.Children.Add(Heading("Account sync"));
        panel.Children.Add(new TextBlock { Text = "Sync chats, projects, memories, appearance and game progress with this account. Keys, tool permissions, local folders and temporary side chats stay on this device.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        var enable = new ToggleSwitch { Header = "Sync account data", IsOn = workspace.SyncEnabled };
        AutomationProperties.SetAutomationId(enable, "AccountSyncEnabled");
        enable.Toggled += async (_, _) => {
            workspace.SetSetting("native.accountSync", enable.IsOn ? "true" : "false");
            if (!enable.IsOn) { syncRun?.Cancel(); syncMessage = "Sync is off. Your local data remains available."; }
            UpdateSyncControls(); if (enable.IsOn) await SyncAccount(true);
        };
        syncStatus = new TextBlock { TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetAutomationId(syncStatus, "AccountSyncStatus");
        syncButton = Button("Sync now", () => SyncAccount(true)); syncReview = Button("Review differences", ReviewSyncConflict);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(syncButton); actions.Children.Add(syncReview);
        panel.Children.Add(enable); panel.Children.Add(actions); panel.Children.Add(syncStatus); UpdateSyncControls();
    }
    void UpdateSyncControls()
    {
        if (workspace.AccountId.Length == 0) return;
        if (syncStatus is not null) syncStatus.Text = syncMessage.Length > 0 ? syncMessage : workspace.SyncEnabled ? "Ready to sync." : "Sync is off on this device.";
        if (syncButton is not null) syncButton.IsEnabled = workspace.SyncEnabled && syncRun is null;
        if (syncReview is not null) syncReview.Visibility = workspace.SyncConflicts().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task SyncAccount(bool manual)
    {
        if (workspace.AccountId.Length == 0 || !workspace.SyncEnabled || syncRun is not null) return;
        if (!SafeToSync()) { if (manual) { syncMessage = "Finish the current conversation or game before syncing."; UpdateSyncControls(); } return; }
        using var cancellation = new CancellationTokenSource(); syncRun = cancellation; var generation = sessionGeneration;
        syncMessage = "Synchronizing account…"; UpdateSyncControls();
        try {
            var result = await workspace.Synchronize(cancellation.Token, () => generation == sessionGeneration && SafeToSync());
            if (generation != sessionGeneration) return;
            syncMessage = result.Offline ? $"Account service unavailable · {result.Queued} queued" : result.Conflicts > 0 ? $"{result.Conflicts} differences to review · {result.Queued} queued" : result.Queued > 0 ? $"{result.Queued} changes will retry automatically" : "Account data is up to date.";
            if (result.Pulled > 0) RefreshSyncedViews();
        } catch (OperationCanceledException) {
            if (generation == sessionGeneration && workspace.SyncEnabled) syncMessage = "Sync will retry when the workspace is idle.";
        } catch (Exception error) {
            if (generation == sessionGeneration) syncMessage = error.Message;
        } finally {
            if (ReferenceEquals(syncRun, cancellation)) syncRun = null;
            if (generation == sessionGeneration) UpdateSyncControls();
        }
    }
    void RefreshSyncedViews()
    {
        // The game view already saved before navigation. Discard its cached copy without saving again.
        if (games is not null && !ReferenceEquals(Body.Content, games)) { games.DiscardCachedView(); games = null; }
        KronoDesign.ApplyTypography(workspace, this); ApplyTheme(workspace.Setting("native.theme"));
        if (chatId is not null && workspace.Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", chatId).Count == 0) chatId = null;
        if (ReferenceEquals(Body.Content, chatView)) { ReloadChats(); LoadChat(); }
    }
    async Task ReviewSyncConflict()
    {
        if (syncRun is not null || !SafeToSync()) return;
        var differences = workspace.SyncConflicts(); if (differences.Count == 0) return;
        var generation = sessionGeneration; reviewingSync = true;
        try {
            var picker = new KronoComboBox { ItemsSource = differences.Select(item => item.Table.Replace('_', ' ') + " · " + item.Id).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var local = new KronoTextBox { Header = "This device", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 135 };
            var cloud = new KronoTextBox { Header = "Cloud", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 135 };
            void Compare() { var item = differences[Math.Max(0, picker.SelectedIndex)]; local.Text = Describe(item.Local); cloud.Text = Describe(item.Cloud); }
            picker.SelectionChanged += (_, _) => Compare(); Compare();
            var content = new StackPanel { Spacing = 10 }; content.Children.Add(picker); content.Children.Add(local); content.Children.Add(cloud);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Resolve account differences", Content = content, PrimaryButtonText = "Use this device", SecondaryButtonText = "Use cloud", CloseButtonText = "Later", DefaultButton = ContentDialogButton.Close };
            var answer = await dialog.ShowAsync();
            if (generation != sessionGeneration || answer == ContentDialogResult.None) return;
            workspace.ResolveSyncConflict(differences[Math.Max(0, picker.SelectedIndex)], answer == ContentDialogResult.Primary); RefreshSyncedViews();
        } finally { reviewingSync = false; }
        await SyncAccount(true);
    }
    static string Describe(string? payload)
    {
        if (payload is null) return "This record was removed.";
        var data = JsonNode.Parse(payload)!.AsObject();
        if (data["deleted_at"] is not null) return "Deleted";
        if (data["content"] is { } content) return content.ToString();
        if (data["value"] is { } value) return value.ToString();
        if (data["state"] is { } save) return $"{data["game_id"]}\nScore: {save["Score"]}\nMoves: {save["Moves"]}";
        return string.Join("\n", data.Where(pair => pair.Key is "name" or "description" or "game_id" or "score" or "project_id").Select(pair => $"{pair.Key.Replace('_', ' ')}: {pair.Value}"));
    }
}
