using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly Dictionary<string, JsonArray> discoveredModels = new(StringComparer.Ordinal);
    readonly TextBlock currentModelLabel = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };

    HashSet<string> HiddenModels(string providerId)
    {
        var stored = workspace.Setting("native.hiddenModels." + providerId);
        return stored.Length == 0 ? [] : JsonSerializer.Deserialize<HashSet<string>>(stored) ?? [];
    }

    string ChatReasoningKey => $"native.reasoning.{provider}.{model}";
    string ChatFastModeKey => $"native.fastMode.{provider}.{model}";
    static bool IsReasoningEffort(string? value) => value is "none" or "low" or "medium" or "high" or "xhigh" or "max";

    void RefreshChatModelLabel()
    {
        currentModelLabel.Text = model;
        currentModelLabel.Visibility = model.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(currentModelLabel, ProviderCatalog.Name(provider) + " / " + model);
    }

    async Task<JsonArray> DiscoverModels(string id, bool refresh = false)
    {
        var endpoint = workspace.Setting("native.endpoint." + id) is { Length: > 0 } custom ? custom : ProviderCatalog.Endpoint(id);
        var key = workspace.Key(id);
        if (key.Length == 0 && ProviderCatalog.RequiresKey(id)) return [];
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
        if (!ProviderCatalog.AutomaticLocal(id)) names = names.Append(selected);
        return names.Where(name => name.Length > 0).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    void SelectChatModel(string selected)
    {
        model = selected;
        workspace.SetSetting("native.model." + provider, model);
        RefreshChatModelLabel();
    }

    bool choosingModel;
    async Task ChooseChatModel()
    {
        if (runId is not null || worker is null || choosingModel) return;
        choosingModel = true;
        try {
            var id = provider;
            var generation = sessionGeneration;
            var available = discoveredModels.GetValueOrDefault(id) ?? [];
            if (available.Count == 0 || ProviderCatalog.AutomaticLocal(id)) {
                try { available = await DiscoverModels(id); }
                catch (Exception error) { Error(error); }
            }
            if (generation != sessionGeneration || id != provider) return;
            var capabilities = available.FirstOrDefault(item => item?["id"]?.ToString() == model)?["capabilities"];
            var supportsEffort = capabilities?["supportsReasoningEffort"]?.GetValue<bool>() == true;
            var levels = capabilities?["reasoningEfforts"]?.AsArray().Select(entry => entry?.ToString())
                .Where(IsReasoningEffort).Select(entry => entry!).ToArray() ?? ["low", "medium", "high"];
            var efforts = new[] { "default" }.Concat(levels).ToArray();
            var labels = efforts.Select(level => level switch {
                "xhigh" => "Extra high", "max" => "Maximum", _ => char.ToUpperInvariant(level[0]) + level[1..]
            }).ToArray();
            var saved = workspace.Setting(ChatReasoningKey);
            var initial = Array.FindIndex(efforts, name => name.Equals(saved, StringComparison.OrdinalIgnoreCase));
            var panel = new StackPanel { Width = 330, Spacing = 12 };
            var advanced = Button("Advanced…", () => {});
            var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = "Model options", Content = panel, CloseButtonText = "Done" };
            var openAdvanced = false;
            advanced.Click += (_, _) => { openAdvanced = true; dialog.Hide(); };
            if (supportsEffort) panel.Children.Add(KronoDesign.DiscreteSlider("Reasoning effort", labels, Math.Max(0, initial),
                index => workspace.SetSetting(ChatReasoningKey, efforts[index])));
            else panel.Children.Add(new TextBlock { Text = model.Length == 0 ? "Choose an available model in Advanced." : "This model manages its own reasoning effort.", TextWrapping = TextWrapping.Wrap });
            if (capabilities?["supportsFastMode"]?.GetValue<bool>() == true) {
                var fast = KronoDesign.Toggle("Fast mode", workspace.Setting(ChatFastModeKey) == "true");
                ToolTipService.SetToolTip(fast, "Uses the provider’s higher-priced priority processing tier.");
                fast.Toggled += (_, _) => workspace.SetSetting(ChatFastModeKey, fast.IsOn ? "true" : "false");
                panel.Children.Add(fast);
            }
            panel.Children.Add(advanced);
            await dialog.ShowAsync();
            if (!openAdvanced || generation != sessionGeneration || id != provider) return;

            var options = new StackPanel { Width = 330, Spacing = 12 };
            var picker = new KronoComboBox { Header = "Model", ItemsSource = ModelChoices(id, available, model), SelectedItem = model,
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
            if (await settings.ShowAsync() != ContentDialogResult.Primary || generation != sessionGeneration || id != provider || picker.SelectedItem is not string selected) return;
            SelectChatModel(selected);
            workspace.SetSetting("native.chatContext", scope.SelectedItem?.ToString() ?? contextOptions[0]);
        } finally { choosingModel = false; }
    }
}
