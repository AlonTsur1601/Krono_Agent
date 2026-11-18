using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Krono_Agent;

public sealed partial class MainPage
{
    readonly List<TextAttachment> attachments = [];
    readonly StackPanel attachmentTray = new() { Spacing = 5, Visibility = Visibility.Collapsed };
    string? attachmentChat;
    bool pickingAttachment;
    string? pastedImage;
    void PasteComposerImage(object sender, TextControlPasteEventArgs args)
    {
        if (TryPasteComposerImage()) args.Handled = true;
    }

    bool TryPasteComposerImage()
    {
        try {
            var clipboard = Clipboard.GetContent();
            var hasBitmap = clipboard.Contains(StandardDataFormats.Bitmap);
            var fallback = hasBitmap ? null : ClipboardBitmap.Read();
            if (!hasBitmap && fallback is null) return false;
            _ = AttachClipboardImage(clipboard, fallback);
            return true;
        } catch (Exception error) { Error(error); return true; }
    }

    async Task AttachClipboardImage(DataPackageView clipboard, ClipboardBitmap.Pixels? fallback)
    {
        try {
            var generation = sessionGeneration;
            var target = chatId;
            if (provider != "openai") throw new InvalidOperationException("Choose an OpenAI model that supports images before pasting an image.");
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            if (fallback is not null) {
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, fallback.Width, fallback.Height, 96, 96, fallback.Data);
                await encoder.FlushAsync();
            } else {
                using var input = await (await clipboard.GetBitmapAsync()).OpenReadAsync();
                if (input.Size > 32 * 1024 * 1024) throw new InvalidOperationException("The clipboard image is too large.");
                var decoder = await BitmapDecoder.CreateAsync(input);
                if (decoder.PixelWidth > 4096 || decoder.PixelHeight > 4096) throw new InvalidOperationException("Paste an image no larger than 4096 × 4096 pixels.");
                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
            }
            if (output.Size > 4 * 1024 * 1024) throw new InvalidOperationException("Paste a smaller image (up to 4 MB as PNG).");
            using var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync((uint)output.Size);
            var bytes = new byte[(int)output.Size];
            reader.ReadBytes(bytes);
            if (generation != sessionGeneration || chatId != target) return;
            pastedImage = Convert.ToBase64String(bytes);
            attachmentChat = target;
            RenderAttachments();
        } catch (Exception error) { Error(error); }
    }
    async Task PickAttachments()
    {
        if (pickingAttachment || workspace.AccountId.Length == 0) return;
        if (attachments.Count >= 4) throw new InvalidOperationException("A message can include up to four text files.");
        pickingAttachment = true; var generation = sessionGeneration; var target = chatId;
        try {
            var path = new KronoTextBox { Header = "Or paste a local file path", MinWidth = 340, PlaceholderText = "C:\\…\\notes.txt" }; AutomationProperties.SetAutomationId(path, "AttachmentPath");
            var content = new StackPanel { Spacing = 10 }; content.Children.Add(new TextBlock { Text = "Attach up to four text or source files, 64 KB each.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(path);
            var dialog = new KronoDialog {
                XamlRoot = XamlRoot, Title = "Attach text files", Content = content,
                PrimaryButtonText = "Browse…", SecondaryButtonText = "Attach path", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary, IsSecondaryButtonEnabled = false
            };
            path.TextChanged += (_, _) => dialog.IsSecondaryButtonEnabled = !string.IsNullOrWhiteSpace(path.Text);
            dialog.Opened += (_, _) => path.Focus(FocusState.Programmatic);
            content.Children.Add(Button("Paste image from clipboard", () => {
                dialog.Hide();
                if (!TryPasteComposerImage()) Error(new InvalidOperationException("The clipboard does not contain an image."));
            }));
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
    void ClearAttachments() { attachments.Clear(); pastedImage = null; attachmentChat = null; RenderAttachments(); }
    void RenderAttachments()
    {
        attachmentTray.Children.Clear(); attachmentTray.Visibility = attachments.Count > 0 || pastedImage is not null ? Visibility.Visible : Visibility.Collapsed;
        if (pastedImage is not null) {
            var image = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            image.Children.Add(new TextBlock { Text = "Clipboard image · sent with this message", VerticalAlignment = VerticalAlignment.Center });
            image.Children.Add(Button("Remove image", () => { pastedImage = null; RenderAttachments(); }));
            attachmentTray.Children.Add(image);
        }
        if (attachments.Count == 0) return;
        attachmentTray.Children.Add(new TextBlock { Text = "Text snapshots will be part of this conversation.", FontSize = 12, Opacity = .65 });
        foreach (var file in attachments) {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            chip.Children.Add(new TextBlock { Text = $"{file.Name} · {(file.Bytes / 1024.0):0.#} KB", TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 310, VerticalAlignment = VerticalAlignment.Center });
            var remove = Button("Remove", () => { attachments.Remove(file); RenderAttachments(); }); AutomationProperties.SetName(remove, "Remove " + file.Name); chip.Children.Add(remove); attachmentTray.Children.Add(chip);
        }
    }
}
