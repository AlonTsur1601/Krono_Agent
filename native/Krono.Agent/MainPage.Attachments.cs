using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly List<TextAttachment> attachments = [];
    readonly StackPanel attachmentTray = new() { Spacing = 5, Visibility = Visibility.Collapsed };
    string? attachmentChat;
    bool pickingAttachment;
    async Task PickAttachments()
    {
        if (pickingAttachment || workspace.AccountId.Length == 0) return;
        if (attachments.Count >= 4) throw new InvalidOperationException("A message can include up to four text files.");
        pickingAttachment = true; var generation = sessionGeneration; var target = chatId;
        try {
            var path = new TextBox { Header = "Or paste a local file path", MinWidth = 340, PlaceholderText = "C:\\…\\notes.txt" }; AutomationProperties.SetAutomationId(path, "AttachmentPath");
            var content = new StackPanel { Spacing = 10 }; content.Children.Add(new TextBlock { Text = "Attach up to four text or source files, 64 KB each.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(path);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Attach text files", Content = content, PrimaryButtonText = "Browse…", SecondaryButtonText = "Attach path", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.None || generation != sessionGeneration || target != chatId) return;
            string[] paths;
            if (choice == ContentDialogResult.Primary) {
                var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
                foreach (var extension in TextAttachment.Extensions) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance!));
                paths = (await picker.PickMultipleFilesAsync()).Select(file => file.Path).ToArray();
            } else {
                var entered = path.Text.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(entered) || entered.StartsWith("\\\\", StringComparison.Ordinal)) throw new InvalidOperationException("Paste the full path of a file on this device.");
                paths = [entered];
            }
            if (generation != sessionGeneration || target != chatId || paths.Length == 0) return;
            if (paths.Length > 4 - attachments.Count) throw new InvalidOperationException("A message can include up to four text files.");
            var selected = new List<TextAttachment>();
            foreach (var file in paths) selected.Add(await TextAttachment.Read(file));
            if (generation != sessionGeneration || target != chatId) return;
            attachmentChat = target; attachments.AddRange(selected); RenderAttachments();
        } finally { pickingAttachment = false; }
    }
    void ClearAttachments() { attachments.Clear(); attachmentChat = null; RenderAttachments(); }
    void RenderAttachments()
    {
        attachmentTray.Children.Clear(); attachmentTray.Visibility = attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (attachments.Count == 0) return;
        attachmentTray.Children.Add(new TextBlock { Text = "Text snapshots will be part of this conversation.", FontSize = 12, Opacity = .65 });
        foreach (var file in attachments) {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            chip.Children.Add(new TextBlock { Text = $"{file.Name} · {(file.Bytes / 1024.0):0.#} KB", TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 310, VerticalAlignment = VerticalAlignment.Center });
            var remove = Button("Remove", () => { attachments.Remove(file); RenderAttachments(); }); AutomationProperties.SetName(remove, "Remove " + file.Name); chip.Children.Add(remove); attachmentTray.Children.Add(chip);
        }
    }
}
