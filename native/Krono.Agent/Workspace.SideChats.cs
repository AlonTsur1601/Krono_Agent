using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed partial class Workspace
{
    public const long SideLifetime = 24 * 60 * 60 * 1000L;
    public string CreateSideChat(string parent, string? throughMessage = null)
    {
        PurgeExpiredSideChats();
        var source = Query("SELECT project_id,name FROM chats WHERE id=$p0 AND deleted_at IS NULL AND id NOT IN (SELECT chat_id FROM side_chats)", parent).FirstOrDefault()
            ?? throw new InvalidOperationException("Choose a normal conversation before creating a side chat.");
        if (throughMessage is not null && Query("SELECT id FROM messages WHERE id=$p0 AND chat_id=$p1 AND deleted_at IS NULL", throughMessage, parent).Count == 0) throw new InvalidOperationException("The branching message is unavailable.");
        var project = source["project_id"]?.ToString();
        var projectConfig = project is null ? "" : Setting("project." + project);
        var context = new JsonObject { ["instructions"] = projectConfig.Length == 0 ? "" : JsonNode.Parse(projectConfig)?["instructions"]?.ToString() ?? "", ["memories"] = new JsonArray(Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", project).Select(row => (JsonNode?)JsonValue.Create(row["content"]!.ToString())).ToArray()) };
        Execute("BEGIN");
        try {
            var id = CreateChat(source["project_id"]?.ToString());
            Execute("UPDATE chats SET name=$p0 WHERE id=$p1", "Side · " + source["name"], id);
            Execute("INSERT INTO messages(id,chat_id,role,content,created_at,version,deleted_at) SELECT lower(hex(randomblob(16))),$p0,role,content,created_at,version,deleted_at FROM messages WHERE chat_id=$p1 AND deleted_at IS NULL AND ($p2 IS NULL OR rowid<=(SELECT rowid FROM messages WHERE id=$p2 AND chat_id=$p1)) ORDER BY created_at,rowid", id, parent, throughMessage);
            Execute("INSERT INTO side_chats(chat_id,parent_chat_id,context,expires_at) VALUES($p0,$p1,$p2,$p3)", id, parent, context.ToJsonString(), Now + SideLifetime);
            Execute("COMMIT"); return id;
        } catch { Execute("ROLLBACK"); throw; }
    }
    public void PurgeExpiredSideChats()
    {
        Execute("BEGIN");
        try {
            Execute("DELETE FROM messages WHERE chat_id IN (SELECT chat_id FROM side_chats WHERE expires_at<=$p0)", Now);
            Execute("DELETE FROM chats WHERE id IN (SELECT chat_id FROM side_chats WHERE expires_at<=$p0)", Now);
            Execute("COMMIT");
        } catch { Execute("ROLLBACK"); throw; }
    }
    public void KeepSideChat(string id)
    {
        if (Query("SELECT chat_id FROM side_chats WHERE chat_id=$p0 AND expires_at>$p1", id, Now).Count == 0) throw new InvalidOperationException("This side chat expired.");
        Execute("DELETE FROM side_chats WHERE chat_id=$p0", id);
    }
    public void QueueSideTransfer(string parent, string content) {
        if (Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", parent).Count == 0) throw new InvalidOperationException("The parent conversation is no longer available.");
        Execute("INSERT INTO side_transfers(id,parent_chat_id,content,created_at) VALUES($p0,$p1,$p2,$p3)", Guid.NewGuid().ToString("N"), parent, content, Now);
    }
    public void FlushSideTransfers()
    {
        Execute("BEGIN");
        try {
            foreach (var transfer in Query("SELECT id,parent_chat_id,content FROM side_transfers ORDER BY created_at,rowid")) {
                var parent = transfer["parent_chat_id"]!.ToString();
                if (Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", parent).Count > 0) AddMessage(parent, "user", "Context transferred from a side chat:\n\n" + transfer["content"]);
                Execute("DELETE FROM side_transfers WHERE id=$p0", transfer["id"]!.ToString());
            }
            Execute("COMMIT");
        } catch { Execute("ROLLBACK"); throw; }
    }
}
