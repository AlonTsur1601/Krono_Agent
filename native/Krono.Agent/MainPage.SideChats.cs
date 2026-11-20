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
            var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).FirstOrDefault()?["project_id"]?.ToString();
            var id = existing ?? workspace.CreateSideChat(chatId, messageId);
            CloseSideChat();
            sideChat = new SideChatPanel(workspace, id, () => {
                return new JsonObject { ["providerId"] = provider, ["apiKey"] = ConnectionKey(), ["modelId"] = model, ["endpoint"] = ProviderEndpoint, ["toolsEnabled"] = false, ["instructions"] = workspace.Instructions(project) };
            }, CloseSideChat, OpenNormalChat, () => { if (runId is null) { workspace.FlushSideTransfers(); ReloadChats(); LoadChat(); } });
            KronoDesign.Present(SideBody, sideChat);
            SetSideChatVisible(true);
        } catch (Exception error) { Error(error); }
    }
    void CloseSideChat()
    {
        sideChat?.Dispose(); sideChat = null; SideBody.Content = null;
        SetSideChatVisible(false);
    }
    void SetSideChatVisible(bool visible)
    {
        SideSurface.Visibility = SideGrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var width = visible ? (double.TryParse(workspace.Setting("native.sideWidth"), out var saved) ? Math.Clamp(saved, 300, 620) : 380) : 0;
        SideColumn.Width = new GridLength(width);
    }
    void OpenNormalChat(string id)
    {
        if (workspace.Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", id).Count == 0) throw new InvalidOperationException("The parent conversation is no longer available.");
        ReloadChats();
        if (runId is not null && id != chatId) return; // Never replace a running parent's context.
        chatId = id; if (sideChat?.ParentChatId != id) CloseSideChat(); ShowChats();
    }
    void ResizeSideChat(object sender, DragDeltaEventArgs args)
    {
        var width = Math.Clamp(SideColumn.Width.Value - args.HorizontalChange, 300, Math.Min(620, Math.Max(300, ActualWidth - 720)));
        SideColumn.Width = new GridLength(width); workspace.SetSetting("native.sideWidth", width.ToString());
    }
}
