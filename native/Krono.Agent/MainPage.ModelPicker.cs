using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly Dictionary<string, JsonArray> discoveredModels = new(StringComparer.Ordinal);
    readonly DispatcherTimer modelDiscoveryTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool discoveringChatModel;
    (string Provider, string Model)? absentLocalModel;
    string reasoningEffort = "";

    HashSet<string> HiddenModels(string providerId)
    {
        var stored = workspace.Setting("native.hiddenModels." + providerId);
        return stored.Length == 0 ? [] : JsonSerializer.Deserialize<HashSet<string>>(stored) ?? [];
    }

    string ChatReasoningKey => $"native.reasoning.{provider}.{model}";
    string? ComposerProject => chatId is null ? draftProject :
        workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).FirstOrDefault()?["project_id"]?.ToString();

    static string ComposerDefaultsKey(string? project) => "native.composerDefaults." + (project ?? "unassigned");

    Workspace.ChatOptions CurrentChatOptions() => new(provider, model, mode, permissions, reasoningEffort,
        workspace.Setting("native.chatContext"), workspace.Setting(ChatFastModeKey) == "true");

    void RememberChatChoices()
    {
        if (workspace.AccountId.Length == 0) return;
        var options = CurrentChatOptions();
        workspace.SetSetting(ComposerDefaultsKey(ComposerProject), JsonSerializer.Serialize(options));
        if (chatId is not null) workspace.SaveChatOptions(chatId, options);
    }

    Workspace.ChatOptions? ComposerDefaults(string? project)
    {
        var stored = workspace.Setting(ComposerDefaultsKey(project));
        if (stored.Length > 0) return JsonSerializer.Deserialize<Workspace.ChatOptions>(stored);
        var recent = workspace.Query("""
            SELECT c.id FROM chats c JOIN settings s ON s.key='native.chatOptions.' || c.id
            WHERE c.account_id=$p0 AND c.project_id IS $p1 AND c.deleted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM side_chats sc WHERE sc.chat_id=c.id)
            ORDER BY c.updated_at DESC,c.id LIMIT 1
            """, workspace.AccountId, project).FirstOrDefault()?["id"]?.ToString();
        return recent is null ? null : workspace.GetChatOptions(recent);
    }
    string ChatFastModeKey => $"native.fastMode.{provider}.{model}";
    static bool IsReasoningEffort(string? value) => value is "none" or "low" or "medium" or "high" or "xhigh" or "max";

    bool IsLocalModelProvider(string id) => ProviderCatalog.AutomaticLocal(id) || Uri.TryCreate(
        ModelEndpoint(id),
        UriKind.Absolute, out var endpoint) && ProviderCatalog.Local(endpoint);

    string ModelEndpoint(string id) => workspace.Setting("native.endpoint." + id) is { Length: > 0 } custom
        ? custom : ProviderCatalog.Endpoint(id);

    IEnumerable<string> ChatProviders() => new[] { provider, workspace.Setting("native.catalogProvider"), "local" }
        .Concat(ProviderCatalog.Ids.Where(id => workspace.Key(id).Length > 0 || workspace.Setting("native.catalogEnabled." + id) == "true"))
        .Where(id => id.Length > 0).Distinct(StringComparer.Ordinal);

    async void RetryChatModel(object? sender, object args)
    {
        if (choosingModel || discoveringChatModel || runId is not null || !ReferenceEquals(Body.Content, chatView)) return;
        if (model.Length == 0) await SelectInitialModel();
        else if (IsLocalModelProvider(provider)) await RefreshLocalChatModels();
    }

    async Task<JsonArray> RefreshLocalChatModels()
    {
        var id = provider;
        var previousModel = model;
        var initialChat = chatId;
        var generation = sessionGeneration;
        if (discoveringChatModel) return discoveredModels.GetValueOrDefault(id) ?? [];
        discoveringChatModel = true;
        try {
            JsonArray available;
            try { available = await DiscoverModels(id, refresh: true); }
            catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { available = []; }
            if (generation == sessionGeneration && id == provider && model == previousModel && chatId == initialChat &&
                runId is null && ReferenceEquals(Body.Content, chatView) &&
                !ModelChoices(id, available, "", false).Contains(model, StringComparer.Ordinal)) {
                absentLocalModel = (id, model);
                model = "";
                ChooseDefaultModel(available);
                RefreshChatModelLabel();
            }
            return available;
        } finally { discoveringChatModel = false; }
    }

    void RefreshChatModelLabel()
    {
        var label = model.Length == 0 ? "Choose model" : model;
        modelChoice.Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetName(modelChoice, label);
        ToolTipService.SetToolTip(modelChoice, model.Length == 0 ? label : ProviderCatalog.Name(provider) + " / " + model);
    }

    async Task<JsonArray> DiscoverModels(string id, bool refresh = false)
    {
        var endpoint = workspace.Setting("native.endpoint." + id) is { Length: > 0 } custom ? custom : ProviderCatalog.Endpoint(id);
        var key = workspace.Key(id);
        if (key.Length == 0 && ProviderCatalog.RequiresKey(id, endpoint)) return [];
        if (!refresh && !ProviderCatalog.AutomaticLocal(id) && discoveredModels.TryGetValue(id, out var cached)) return cached;
        if (worker is null) return [];
        var generation = sessionGeneration;
        var result = await worker.Request(Guid.NewGuid().ToString(), "models",
            new { providerId = id, apiKey = key, endpoint, toolsEnabled = false });
        var models = result?.AsArray() ?? [];
        if (generation == sessionGeneration) discoveredModels[id] = models;
        return models;
    }

    string[] ModelChoices(string id, JsonArray available, string selected, bool honorHidden = true)
    {
        var hidden = honorHidden ? HiddenModels(id) : [];
        var names = available.Select(entry => entry?["id"]?.ToString() ?? "").Where(name => !hidden.Contains(name) || name == selected);
        return names.Append(selected).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    async Task SelectInitialModel()
    {
        var initialProvider = provider;
        var generation = sessionGeneration;
        var initialChat = chatId;
        var origin = loadedContext;
        bool Current() => generation == sessionGeneration && provider == initialProvider && model.Length == 0 &&
            chatId == initialChat && origin == loadedContext && ReferenceEquals(Body.Content, chatView);
        if (discoveringChatModel || worker is null || !Current()) return;
        discoveringChatModel = true;
        try {
            var candidates = absentLocalModel is { } absent && absent.Provider == initialProvider
                ? [initialProvider] : ChatProviders().ToArray();
            foreach (var candidate in candidates) {
                JsonArray available;
                try { available = await DiscoverModels(candidate, refresh: IsLocalModelProvider(candidate)); }
                catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { continue; }
                if (!Current()) return;
                if (ModelChoices(candidate, available, "").Length == 0) continue;
                provider = candidate;
                ChooseDefaultModel(available);
                return;
            }
        } finally { discoveringChatModel = false; }
    }

    void ChooseDefaultModel(JsonArray available)
    {
        if (model.Length != 0) return;
        var choices = ModelChoices(provider, available, "");
        var remembered = absentLocalModel is { } absent && absent.Provider == provider
            ? absent.Model : workspace.Setting("native.model." + provider);
        var first = choices.Contains(remembered, StringComparer.Ordinal) ? remembered : choices.FirstOrDefault();
        if (first is null) return;
        model = first;
        absentLocalModel = null;
        if (emptyReasoningFlyout) reasoningFlyout?.Hide();
        RefreshChatModelLabel();
    }

    void SelectChatModel(string selected)
    {
        if (selected.Length == 0) return;
        model = selected;
        absentLocalModel = null;
        RememberChatChoices();
        RefreshChatModelLabel();
    }

    Flyout? reasoningFlyout;
    bool emptyReasoningFlyout;
    bool choosingModel;
    async Task ChooseChatModel()
    {
        if (runId is not null || worker is null || choosingModel) return;
        if (reasoningFlyout is not null) { reasoningFlyout.Hide(); return; }
        choosingModel = true;
        try {
            var generation = sessionGeneration;
            if (model.Length == 0) await SelectInitialModel();
            var id = provider;
            var available = discoveredModels.GetValueOrDefault(id) ?? [];
            if (model.Length > 0 && IsLocalModelProvider(id)) available = await RefreshLocalChatModels();
            else if (model.Length > 0 && available.Count == 0) {
                try { available = await DiscoverModels(id); }
                catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { }
            }
            if (generation != sessionGeneration || id != provider || !ReferenceEquals(Body.Content, chatView) || !modelChoice.IsLoaded) return;
            var panel = new StackPanel { Width = 270, Spacing = 10 };
            if (model.Length == 0) {
                panel.Children.Add(new TextBlock { Text = "No models available", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = "Add a model in Settings to start a conversation.", TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(Button("Model settings", () => {
                    reasoningFlyout?.Hide();
                    OpenModelSettings();
                }));
                ShowReasoningFlyout(panel, empty: true);
                return;
            }
            var capabilities = available.FirstOrDefault(item => item?["id"]?.ToString() == model)?["capabilities"];
            var supportsEffort = capabilities?["supportsReasoningEffort"]?.GetValue<bool>() == true;
            var levels = capabilities?["reasoningEfforts"]?.AsArray().Select(entry => entry?.ToString())
                .Where(IsReasoningEffort).Select(entry => entry!).ToArray() ?? ["low", "medium", "high"];
            var efforts = new[] { "default" }.Concat(levels).ToArray();
            var labels = efforts.Select(level => level switch {
                "xhigh" => "Extra high", "max" => "Maximum", _ => char.ToUpperInvariant(level[0]) + level[1..]
            }).ToArray();
            var saved = reasoningEffort;
            var initial = Array.FindIndex(efforts, name => name.Equals(saved, StringComparison.OrdinalIgnoreCase));
            var advanced = Button("Advanced…", async () => {
                reasoningFlyout?.Hide();
                await ChooseAdvancedModel(id, generation, available);
            });
            if (supportsEffort) panel.Children.Add(KronoDesign.DiscreteSlider("Reasoning effort", labels, Math.Max(0, initial),
                index => { reasoningEffort = efforts[index]; RememberChatChoices(); }));
            else panel.Children.Add(new TextBlock { Text = "This model manages its reasoning effort.", TextWrapping = TextWrapping.Wrap });
            if (capabilities?["supportsFastMode"]?.GetValue<bool>() == true) {
                var fast = KronoDesign.Toggle("Fast mode", workspace.Setting(ChatFastModeKey) == "true");
                ToolTipService.SetToolTip(fast, "Uses the provider’s higher-priced priority processing tier.");
                fast.Toggled += (_, _) => workspace.SetSetting(ChatFastModeKey, fast.IsOn ? "true" : "false");
                panel.Children.Add(fast);
            }
            panel.Children.Add(advanced);
            ShowReasoningFlyout(panel);
        } finally { choosingModel = false; }
    }

    void ShowReasoningFlyout(StackPanel panel, bool empty = false)
    {
        AutomationProperties.SetAutomationId(panel, "ReasoningPanel");
        var flyout = new Flyout {
            Content = panel,
            FlyoutPresenterStyle = (Style)Application.Current.Resources["KronoPopoverPresenter"],
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft
        };
        flyout.Closed += (_, _) => {
            if (!ReferenceEquals(reasoningFlyout, flyout)) return;
            reasoningFlyout = null;
            emptyReasoningFlyout = false;
        };
        reasoningFlyout = flyout;
        emptyReasoningFlyout = empty;
        KronoDesign.RefreshTypography(panel);
        flyout.ShowAt(modelChoice);
    }

    async Task ChooseAdvancedModel(string id, int generation, JsonArray available)
    {
        if (generation != sessionGeneration || id != provider) return;
        var origin = loadedContext;
        var optionsByProvider = await Task.WhenAll(ChatProviders().Select(async candidate => {
            try {
                var catalog = candidate == id ? available : await DiscoverModels(candidate);
                return ModelChoices(candidate, catalog, "").Select(name => (Provider: candidate, Name: name)).ToArray();
            } catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException) { return []; }
        }));
        if (generation != sessionGeneration || origin != loadedContext || !ReferenceEquals(Body.Content, chatView)) return;
        var entries = optionsByProvider.SelectMany(group => group).ToArray();
        var labels = entries.Select(entry => entries.Count(other => other.Name == entry.Name) > 1
            ? entry.Name + " · " + ProviderCatalog.Name(entry.Provider) : entry.Name).ToArray();
        var options = new StackPanel { Width = 330, Spacing = 12 };
        var picker = new KronoComboBox { Header = "Model", ItemsSource = labels, SelectedIndex = Array.IndexOf(entries, (id, model)),
            HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose an available model" };
        AutomationProperties.SetAutomationId(picker, "ChatModelPicker");
        options.Children.Add(picker);
        var contextOptions = new[] { "Full conversation", "Recent messages" };
        var scope = Choices("Conversation context", contextOptions, workspace.Setting("native.chatContext") == contextOptions[1] ? contextOptions[1] : contextOptions[0], _ => { });
        options.Children.Add(scope);
        var settings = new KronoDialog { XamlRoot = XamlRoot, Title = "Advanced model options", Content = options,
            PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = picker.SelectedItem is string };
        picker.SelectionChanged += (_, _) => settings.IsPrimaryButtonEnabled = picker.SelectedItem is string;
        if (await settings.ShowAsync() != ContentDialogResult.Primary || generation != sessionGeneration || origin != loadedContext || picker.SelectedIndex < 0) return;
        var selected = entries[picker.SelectedIndex];
        provider = selected.Provider;
        SelectChatModel(selected.Name);
        workspace.SetSetting("native.chatContext", scope.SelectedItem?.ToString() ?? contextOptions[0]);
    }
}
