using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed partial class MainPage
{
    SideChatPanel? sideChat;
    void OpenSideChat() => OpenSideChatFrom(null);
    void OpenSideChatFrom(string? messageId)
    {
        try {
            if (chatId is null) throw new InvalidOperationException("Start a conversation before opening a side chat.");
            if (messageId is null && sideChat?.ParentChatId == chatId) { sideChat.FocusComposer(); return; }
            var existing = messageId is null ? workspace.Query("SELECT chat_id FROM side_chats WHERE parent_chat_id=$p0 AND expires_at>$p1 ORDER BY expires_at DESC LIMIT 1", chatId, Workspace.Now).FirstOrDefault()?["chat_id"]?.ToString() : null;
            var id = existing ?? workspace.CreateSideChat(chatId, messageId);
            CloseSideChat();
            sideChat = new SideChatPanel(workspace, id, () => {
                if (model.Length == 0) throw new InvalidOperationException("Choose a model in Settings first.");
                return new JsonObject { ["providerId"] = provider, ["apiKey"] = workspace.Key(provider), ["modelId"] = model, ["endpoint"] = ProviderEndpoint, ["toolsEnabled"] = false };
            }, CloseSideChat, OpenNormalChat, () => { if (runId is null) { workspace.FlushSideTransfers(); ReloadChats(); LoadChat(); } });
            SideSurface.Visibility = SideGrip.Visibility = Visibility.Visible;
            SideColumn.Width = new GridLength(double.TryParse(workspace.Setting("native.sideWidth"), out var width) ? Math.Clamp(width, 300, 620) : 380);
            KronoDesign.Present(SideBody, sideChat);
            if (chatView is not null) { ((FrameworkElement)chatView.Children[0]).Visibility = Visibility.Collapsed; chatView.ColumnDefinitions[0].Width = new GridLength(0); }
        } catch (Exception error) { Error(error); }
    }
    void CloseSideChat()
    {
        sideChat?.Dispose(); sideChat = null; SideBody.Content = null;
        SideSurface.Visibility = SideGrip.Visibility = Visibility.Collapsed; SideColumn.Width = new GridLength(0);
        if (chatView is not null) { ((FrameworkElement)chatView.Children[0]).Visibility = Visibility.Visible; chatView.ColumnDefinitions[0].Width = GridLength.Auto; }
    }
    void OpenNormalChat(string id)
    {
        if (workspace.Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", id).Count == 0) throw new InvalidOperationException("The parent conversation is no longer available.");
        ReloadChats();
        if (runId is not null) return; // Never replace a running parent's view or context.
        chatId = id; CloseSideChat(); ShowChats();
    }
    void ResizeSideChat(object sender, DragDeltaEventArgs args)
    {
        var width = Math.Clamp(SideColumn.Width.Value - args.HorizontalChange, 300, Math.Min(620, Math.Max(300, ActualWidth - 720)));
        SideColumn.Width = new GridLength(width); workspace.SetSetting("native.sideWidth", width.ToString());
    }
}
