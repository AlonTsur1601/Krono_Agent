using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Krono_Agent;

public sealed partial class MainPage
{
    HashSet<string> HiddenModels(string providerId)
    {
        var stored = workspace.Setting("native.hiddenModels." + providerId);
        return stored.Length == 0 ? [] : JsonSerializer.Deserialize<HashSet<string>>(stored) ?? [];
    }

    string ChatReasoningKey => $"native.reasoning.{provider}.{model}";

    void RefreshChatModelLabel()
    {
        var label = model.Length == 0 ? "Choose model" : model;
        modelChoice.Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modelChoice, label);
        ToolTipService.SetToolTip(modelChoice, ProviderCatalog.Name(provider) + " / " + model);
    }

    async Task ChooseChatModel()
    {
        if (runId is not null || worker is null) return;
        var originalProvider = provider;
        var generation = sessionGeneration;
        var hidden = HiddenModels(originalProvider);
        var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(ActualWidth - 120, 300, 430) };
        panel.Children.Add(new TextBlock { Text = ProviderCatalog.Name(provider) });
        var picker = new KronoComboBox { Header = "Model", IsEditable = true, Text = model, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(picker, "ChatModelPicker");
        panel.Children.Add(picker);
        var effort = Choices("Reasoning effort", ["Default", "low", "medium", "high"], "Default", _ => { });
        var contextOptions = new[] { "Full conversation", "Recent messages" };
        var scope = Choices("Conversation context", contextOptions, workspace.Setting("native.chatContext") == contextOptions[1] ? contextOptions[1] : contextOptions[0], _ => { });
        var context = new TextBlock { TextWrapping = TextWrapping.Wrap };
        JsonArray? available = null;
        string? effortModel = null;
        void RefreshCapabilities(string id)
        {
            var capabilities = available?.FirstOrDefault(entry => entry?["id"]?.ToString() == id)?["capabilities"];
            effort.IsEnabled = capabilities?["supportsReasoningEffort"]?.GetValue<bool>() == true;
            effortModel = effort.IsEnabled ? id : null;
            var savedEffort = workspace.Setting($"native.reasoning.{originalProvider}.{id}");
            effort.SelectedItem = savedEffort is "low" or "medium" or "high" ? savedEffort : "Default";
            var tokens = capabilities?["contextWindow"]?.GetValue<int>() ?? 0;
            context.Text = tokens > 0 ? $"Model context window: {tokens:N0} tokens." : "";
            context.Visibility = tokens > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        picker.SelectionChanged += (_, _) => RefreshCapabilities(picker.SelectedItem as string ?? picker.Text.Trim());
        picker.TextSubmitted += (_, args) => RefreshCapabilities(args.Text.Trim());
        var providerWorker = worker;
        var key = workspace.Key(originalProvider);
        var endpoint = ProviderEndpoint;
        var toolsEnabled = ProviderToolsEnabled;
        var pickerOpen = true;
        var loadModelFeedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        async Task LoadAvailableModels()
        {
            loadModelFeedback.Text = "Loading models…";
            try {
                var response = await providerWorker.Request(Guid.NewGuid().ToString(), "models", new { providerId = originalProvider, apiKey = key, endpoint, toolsEnabled });
                if (!pickerOpen || generation != sessionGeneration) return;
                var selected = picker.Text;
                available = response?.AsArray();
                picker.ItemsSource = available?.Select(entry => entry!["id"]!.ToString()).Where(id => !hidden.Contains(id)).ToArray();
                picker.Text = selected;
                RefreshCapabilities(selected.Trim());
                loadModelFeedback.Visibility = Visibility.Collapsed;
            } catch (Exception) {
                if (pickerOpen) loadModelFeedback.Text = "Could not load available models. You can enter a model name.";
            }
        }
        panel.Children.Add(loadModelFeedback);
        panel.Children.Add(effort);
        panel.Children.Add(scope);
        panel.Children.Add(context);
        RefreshCapabilities(model);
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = "Model and context", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        _ = LoadAvailableModels();
        var result = await dialog.ShowAsync();
        pickerOpen = false;
        if (result != ContentDialogResult.Primary || generation != sessionGeneration || originalProvider != provider) return;
        var nextModel = picker.Text.Trim();
        if (nextModel.Length == 0) { Error(new InvalidOperationException("Enter or choose a model.")); return; }
        if (hidden.Contains(nextModel) && nextModel != model) {
            Error(new InvalidOperationException("Enable this model in Settings first."));
            return;
        }
        var selectedModelSupportsEffort = available?.FirstOrDefault(entry => entry?["id"]?.ToString() == nextModel)?["capabilities"]?["supportsReasoningEffort"]?.GetValue<bool>() == true;
        model = nextModel;
        workspace.SetSetting("native.model." + provider, model);
        workspace.SetSetting("native.chatContext", scope.SelectedItem?.ToString() ?? contextOptions[0]);
        if (selectedModelSupportsEffort && effortModel == nextModel)
            workspace.SetSetting(ChatReasoningKey, effort.SelectedItem?.ToString() ?? "Default");
        RefreshChatModelLabel();
    }
}
