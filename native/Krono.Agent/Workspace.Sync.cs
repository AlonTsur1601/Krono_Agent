using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed partial class Workspace
{
    sealed record SyncTable(string Name, string Key, string[] Fields, bool Versioned = true);
    static readonly SyncTable[] SyncTables = [
        new("projects", "id", ["id", "name", "icon", "color", "description", "created_at", "updated_at", "deleted_at"]),
        new("chats", "id", ["id", "project_id", "name", "pinned", "archived", "created_at", "updated_at", "deleted_at"]),
        new("messages", "id", ["id", "chat_id", "role", "content", "created_at", "deleted_at"]),
        new("memories", "id", ["id", "project_id", "content", "created_at", "updated_at", "deleted_at"]),
        new("account_settings", "key", ["key", "value"]),
        new("game_scores", "id", ["id", "game_id", "score", "achieved_at"], false),
        new("game_saves", "game_id", ["game_id", "state", "saved_at"])
    ];
    static readonly HashSet<string> PortableSettings = new(["native.theme", "native.textFont", "native.codeFont", "native.textSize", "native.codeSize", "native.codePalette", "native.gameFavorites", "native.sideWidth"], StringComparer.Ordinal);
    const string ProjectInstructions = "project.instructions:";
    public sealed record SyncSummary(int Pulled, int Pushed, int Queued, int Conflicts, bool Offline);
    public sealed record SyncConflict(string Table, string Id, string Local, string? Cloud, int Revision);
    sealed record SyncRow(JsonObject Payload, int Revision);
    bool syncRunning;
    public bool SyncEnabled => Setting("native.accountSync") == "true";
    static bool Portable(string name) => PortableSettings.Contains(name) || name.StartsWith(ProjectInstructions, StringComparison.Ordinal) && Guid.TryParse(name[ProjectInstructions.Length..], out _);
    public List<SyncConflict> SyncConflicts() => Query("SELECT * FROM sync_conflicts ORDER BY detected_at,table_name,record_id").Select(row => new SyncConflict(row["table_name"]!.ToString(), row["record_id"]!.ToString(), row["local_payload"]!.ToString(), row["cloud_payload"]?.ToString(), (int)row["cloud_revision"]!.GetValue<long>())).ToList();
    public async Task<SyncSummary> Synchronize(CancellationToken token, Func<bool> canApply)
    {
        if (syncRunning) throw new InvalidOperationException("Account synchronization is already running.");
        syncRunning = true;
        var account = AccountId; var pulled = 0; var pushed = 0;
        void Check() { token.ThrowIfCancellationRequested(); if (AccountId != account || !canApply()) throw new OperationCanceledException(); }
        try {
            Check();
            // Persist every pending change before the first HTTP request, including offline runs.
            var batches = SyncTables.Select(table => (table, local: LocalRows(table), checkpoints: Checkpoints(table))).ToArray();
            InSyncTransaction(() => {
                foreach (var (table, local, checkpoints) in batches) foreach (var (id, row) in local) {
                    if (checkpoints.TryGetValue(id, out var previous) && JsonNode.DeepEquals(previous.Payload, row)) continue;
                    QueueSync(table, id, row, checkpoints.ContainsKey(id));
                }
            });
            if (IsOfflineSession || access.Length == 0) return Summary(true);
            foreach (var (table, local, checkpoints) in batches) {
                var cloud = await FetchSyncRows(table, account, token); Check();
                foreach (var id in local.Keys.Concat(cloud.Keys).Distinct(StringComparer.Ordinal)) {
                    Check(); local.TryGetValue(id, out var here); cloud.TryGetValue(id, out var there); checkpoints.TryGetValue(id, out var baseline);
                    switch (SyncMerge.Decide(here, there?.Payload, baseline?.Payload)) {
                        case SyncDecision.Nothing: ClearQueued(table, id); break;
                        case SyncDecision.Acknowledge: Checkpoint(table, id, there!); break;
                        case SyncDecision.Pull:
                            if (JsonNode.DeepEquals(LocalRows(table, id).GetValueOrDefault(id), here)) {
                                InSyncTransaction(() => { ApplySyncRow(table, id, there!.Payload); Checkpoint(table, id, there); }); pulled++;
                            }
                            break;
                        case SyncDecision.Insert:
                        case SyncDecision.Update:
                            if (there is not null && !table.Versioned) { StoreConflict(table, id, here!, there); break; }
                            var written = await PushSyncRow(table, id, here!, there?.Revision, account, token); Check();
                            // A conditional write returning no rows means another device won the race.
                            if (written is not null) { Checkpoint(table, id, written); pushed++; }
                            break;
                        case SyncDecision.Conflict: StoreConflict(table, id, here!, there); break;
                    }
                }
            }
            return Summary(false);
        } catch (HttpRequestException error) when (error.StatusCode is null || (int)error.StatusCode >= 500) { Check(); return Summary(true); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { Check(); return Summary(true); }
        finally { syncRunning = false; }
        SyncSummary Summary(bool offline) => new(pulled, pushed, (int)Query("SELECT count(*) AS n FROM outbox WHERE id LIKE 'cloud:%'")[0]["n"]!.GetValue<long>(), SyncConflicts().Count, offline);
    }
    public void ResolveSyncConflict(SyncConflict conflict, bool keepLocal)
    {
        if (syncRunning) throw new InvalidOperationException("Wait until synchronization finishes.");
        var table = SyncTables.FirstOrDefault(item => item.Name == conflict.Table) ?? throw new InvalidOperationException("Unknown data type.");
        var current = SyncConflicts().FirstOrDefault(item => item.Table == conflict.Table && item.Id == conflict.Id);
        if (current != conflict) throw new InvalidOperationException("This conflict changed. Open the latest comparison.");
        if (!JsonNode.DeepEquals(LocalRows(table, conflict.Id).GetValueOrDefault(conflict.Id), JsonNode.Parse(conflict.Local))) throw new InvalidOperationException("The local record changed. Synchronize again before resolving it.");
        var cloud = conflict.Cloud is null ? null : JsonNode.Parse(conflict.Cloud)!.AsObject();
        InSyncTransaction(() => {
            if (!keepLocal && cloud is not null) ApplySyncRow(table, conflict.Id, cloud);
            if (cloud is not null) Checkpoint(table, conflict.Id, new(cloud, conflict.Revision));
            else if (!keepLocal && table.Fields.Contains("deleted_at") && LocalRows(table, conflict.Id).GetValueOrDefault(conflict.Id) is { } local) {
                local["deleted_at"] = Now; ApplySyncRow(table, conflict.Id, local); Checkpoint(table, conflict.Id, new(local, 0));
            } else {
                if (!keepLocal) RemoveLocalSyncRow(table, conflict.Id);
                Execute("DELETE FROM sync_checkpoints WHERE table_name=$p0 AND record_id=$p1", table.Name, conflict.Id); ClearQueued(table, conflict.Id);
            }
        });
    }
    void InSyncTransaction(Action action) { Execute("BEGIN"); try { action(); Execute("COMMIT"); } catch { Execute("ROLLBACK"); throw; } }
    Dictionary<string, SyncRow> Checkpoints(SyncTable table) => Query("SELECT record_id,payload,revision FROM sync_checkpoints WHERE table_name=$p0", table.Name).ToDictionary(row => row["record_id"]!.ToString(), row => new SyncRow(JsonNode.Parse(row["payload"]!.ToString())!.AsObject(), (int)row["revision"]!.GetValue<long>()), StringComparer.Ordinal);
    void QueueSync(SyncTable table, string id, JsonObject row, bool update) => Execute("INSERT INTO outbox(id,operation,table_name,record_id,payload,created_at) VALUES($p0,$p1,$p2,$p3,$p4,$p5) ON CONFLICT(id) DO UPDATE SET operation=excluded.operation,payload=excluded.payload", "cloud:" + table.Name + ":" + id, row["deleted_at"] is not null ? "delete" : update ? "update" : "insert", table.Name, id, row.ToJsonString(), Now);
    void ClearQueued(SyncTable table, string id) { Execute("DELETE FROM outbox WHERE id=$p0", "cloud:" + table.Name + ":" + id); Execute("DELETE FROM sync_conflicts WHERE table_name=$p0 AND record_id=$p1", table.Name, id); }
    void Checkpoint(SyncTable table, string id, SyncRow row) { Execute("INSERT INTO sync_checkpoints(table_name,record_id,revision,payload) VALUES($p0,$p1,$p2,$p3) ON CONFLICT(table_name,record_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload", table.Name, id, row.Revision, row.Payload.ToJsonString()); ClearQueued(table, id); }
    void StoreConflict(SyncTable table, string id, JsonObject local, SyncRow? cloud) => Execute("INSERT INTO sync_conflicts(table_name,record_id,cloud_revision,cloud_payload,local_payload,detected_at) VALUES($p0,$p1,$p2,$p3,$p4,$p5) ON CONFLICT(table_name,record_id) DO UPDATE SET cloud_revision=excluded.cloud_revision,cloud_payload=excluded.cloud_payload,local_payload=excluded.local_payload", table.Name, id, cloud?.Revision ?? 0, cloud?.Payload.ToJsonString(), local.ToJsonString(), Now);
    Dictionary<string, JsonObject> LocalRows(SyncTable table, string? id = null)
    {
        var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (table.Name == "account_settings") {
            foreach (var row in Query("SELECT key,value FROM settings")) {
                var name = row["key"]!.ToString(); var value = row["value"]!.ToString();
                if (name.StartsWith("project.", StringComparison.Ordinal) && Guid.TryParse(name[8..], out _)) {
                    name = ProjectInstructions + name[8..];
                    try { if (JsonNode.Parse(value)?["instructions"] is not { } instructions) continue; value = instructions.ToString(); } catch (System.Text.Json.JsonException) { continue; }
                }
                if (Portable(name) && ValidSetting(name, value) && (id is null || id == name)) rows[name] = new() { ["key"] = name, ["value"] = value };
            }
            return rows;
        }
        var sql = table.Name == "messages"
            ? "SELECT " + string.Join(',', table.Fields.Select(name => "m." + name)) + " FROM messages m JOIN chats c ON c.id=m.chat_id WHERE c.account_id=$p0 AND c.id NOT IN (SELECT chat_id FROM side_chats) AND ($p1 IS NULL OR m.id=$p1)"
            : "SELECT " + string.Join(',', table.Fields) + " FROM " + table.Name + " WHERE account_id=$p0 AND ($p1 IS NULL OR " + table.Key + "=$p1)" + (table.Name == "chats" ? " AND id NOT IN (SELECT chat_id FROM side_chats)" : table.Name == "game_saves" ? " AND id LIKE 'native-v1:%'" : "");
        foreach (var row in Query(sql, AccountId, id)) {
            if (table.Name == "game_saves") { try { row["state"] = JsonNode.Parse(row["state"]!.ToString()); if (!ValidGamePayload(row)) continue; } catch (System.Text.Json.JsonException) { continue; } }
            var canonical = Canonical(table, row); rows[canonical[table.Key]!.ToString()] = canonical;
        }
        return rows;
    }
    static bool ValidGamePayload(JsonObject row) => row["state"] is JsonObject state && state["Version"]?.ToString() == "1" && state["Game"]?.ToString() == row["game_id"]?.ToString() && state.ToJsonString().Length <= 65536;
    static bool ValidSetting(string name, string value)
    {
        static bool Size(string text, double min, double max) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= min && n <= max;
        return name switch {
            "native.theme" => value is "system" or "dark" or "light" or "halloween",
            "native.codePalette" => value is "classic" or "mint" or "halloween",
            "native.textSize" => Size(value, 11, 28), "native.codeSize" => Size(value, 10, 26), "native.sideWidth" => Size(value, 300, 620),
            "native.gameFavorites" => value.Length <= 4096,
            _ => value.Length <= (name.StartsWith(ProjectInstructions, StringComparison.Ordinal) ? 32768 : 512)
        };
    }
    static JsonObject Canonical(SyncTable table, JsonObject source)
    {
        var row = new JsonObject();
        foreach (var field in table.Fields) {
            var node = source[field];
            if (field is "pinned" or "archived") { row[field] = node?.ToString() is "true" or "True" or "1"; continue; }
            if (field is "created_at" or "updated_at" or "deleted_at" or "saved_at" or "achieved_at" or "score") {
                if (node is null && field == "deleted_at") { row[field] = null; continue; }
                if (!long.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0) throw new InvalidDataException("Cloud record has invalid numeric data.");
                row[field] = value; continue;
            }
            if (field == "state") { row[field] = node?.DeepClone(); continue; }
            if (node is null && field is "project_id" or "icon" or "color" or "description") { row[field] = null; continue; }
            var text = node?.ToString() ?? (field == "name" ? "New chat" : "");
            if (field == table.Key && (text.Length is 0 or > 256 || text.Any(char.IsControl))) throw new InvalidDataException("Cloud record has an invalid identifier.");
            row[field] = text;
        }
        return row;
    }
    void ApplySyncRow(SyncTable table, string id, JsonObject row)
    {
        if (table.Name == "account_settings") {
            var value = row["value"]!.ToString();
            if (id.StartsWith(ProjectInstructions, StringComparison.Ordinal)) {
                var localKey = "project." + id[ProjectInstructions.Length..]; var stored = Setting(localKey); JsonObject config = new();
                if (stored.Length > 0) { try { config = JsonNode.Parse(stored) as JsonObject ?? new(); } catch (System.Text.Json.JsonException) { } }
                config["instructions"] = value; SetSetting(localKey, config.ToJsonString());
            } else if (Portable(id)) SetSetting(id, value);
            return;
        }
        var fields = table.Fields.ToList(); var args = fields.Select(field => field == "state" ? row[field]?.ToJsonString() : row[field]?.ToString()).Cast<object?>().ToList();
        foreach (var field in new[] { "pinned", "archived" }) if (fields.IndexOf(field) is var i && i >= 0) args[i] = row[field]!.GetValue<bool>() ? 1 : 0;
        var keyField = table.Key;
        if (table.Name == "game_saves") { fields.Add("id"); args.Add("native-v1:" + id); keyField = "id"; }
        if (table.Name != "messages") { fields.Add("account_id"); args.Add(AccountId); }
        var updates = fields.Where(field => field != keyField && field != "account_id").Select(field => field + "=excluded." + field);
        Execute($"INSERT INTO {table.Name}({string.Join(',', fields)}) VALUES({string.Join(',', fields.Select((_, i) => "$p" + i))}) ON CONFLICT({keyField}) DO UPDATE SET {string.Join(',', updates)}", args.ToArray());
    }
    void RemoveLocalSyncRow(SyncTable table, string id)
    {
        if (table.Name == "account_settings") {
            if (id.StartsWith(ProjectInstructions, StringComparison.Ordinal)) {
                var localKey = "project." + id[ProjectInstructions.Length..];
                var config = JsonNode.Parse(Setting(localKey))?.AsObject() ?? new JsonObject(); config.Remove("instructions"); SetSetting(localKey, config.ToJsonString());
            } else Execute("DELETE FROM settings WHERE key=$p0", id);
        }
        else if (table.Name == "game_saves") Execute("DELETE FROM game_saves WHERE id=$p0 AND account_id=$p1", "native-v1:" + id, AccountId);
        else if (table.Name == "game_scores") Execute("DELETE FROM game_scores WHERE id=$p0 AND account_id=$p1", id, AccountId);
    }
    async Task<Dictionary<string, SyncRow>> FetchSyncRows(SyncTable table, string account, CancellationToken token)
    {
        var rows = new Dictionary<string, SyncRow>(StringComparer.Ordinal); string? cursor = null;
        var fields = table.Fields.Concat(table.Versioned ? ["account_id", "version"] : ["account_id"]);
        for (var page = 0; page < 1000; page++) {
            var data = await SyncRequest(HttpMethod.Get, $"{table.Name}?account_id=eq.{account}&select={string.Join(',', fields)}&order={table.Key}.asc&limit=200" + (cursor is null ? "" : $"&{table.Key}=" + Filter("gt", cursor)), null, null, token);
            if (data.Count == 0) return rows;
            var previous = cursor;
            foreach (var node in data) {
                var row = node?.AsObject() ?? throw new InvalidDataException("Invalid cloud row."); cursor = row[table.Key]?.ToString() ?? throw new InvalidDataException("Cloud identifier missing.");
                if (row["account_id"]?.ToString() != account) throw new InvalidDataException("Cloud returned a record outside this account.");
                if (table.Name == "account_settings" && (!Portable(cursor) || row["value"] is not JsonValue setting || !setting.TryGetValue<string>(out var text) || !ValidSetting(cursor, text)) || table.Name == "game_saves" && !ValidGamePayload(row)) continue;
                rows[cursor] = new(Canonical(table, row), Revision(table, row));
            }
            if (cursor == previous) throw new InvalidDataException("Cloud pagination did not advance.");
        }
        throw new InvalidDataException("Too many cloud records in one synchronization.");
    }
    static int Revision(SyncTable table, JsonObject row) => !table.Versioned ? 1 : int.TryParse(row["version"]?.ToString(), out var version) && version > 0 ? version : throw new InvalidDataException("Cloud revision missing.");
    static string Filter(string operation, string value) => operation + "." + Uri.EscapeDataString("\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
    async Task<SyncRow?> PushSyncRow(SyncTable table, string id, JsonObject value, int? revision, string account, CancellationToken token)
    {
        var payload = value.DeepClone().AsObject(); payload["account_id"] = account;
        if (table.Versioned) payload["version"] = checked((revision ?? 0) + 1);
        if (table.Name == "account_settings") payload["updated_at"] = Now;
        var endpoint = revision is null ? $"{table.Name}?on_conflict=account_id,{table.Key}" : $"{table.Name}?account_id=eq.{account}&{table.Key}={Filter("eq", id)}&version=eq.{revision}";
        var rows = await SyncRequest(revision is null ? HttpMethod.Post : HttpMethod.Patch, endpoint, payload, revision is null ? "return=representation,resolution=ignore-duplicates" : "return=representation", token);
        if (rows.Count == 0) return null;
        if (rows.Count != 1 || rows[0] is not JsonObject row || row["account_id"]?.ToString() != account || row[table.Key]?.ToString() != id) throw new InvalidDataException("Cloud write acknowledgement did not match its record.");
        return new(Canonical(table, row), Revision(table, row));
    }
    async Task<JsonArray> SyncRequest(HttpMethod method, string endpoint, JsonObject? body, string? prefer, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, url.TrimEnd('/') + "/rest/v1/" + endpoint);
        request.Headers.Add("apikey", key); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (prefer is not null) request.Headers.Add("Prefer", prefer);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) {
            var message = response.StatusCode switch { HttpStatusCode.Unauthorized => "Your account session needs to refresh before syncing.", HttpStatusCode.Forbidden => "Account synchronization is blocked by the project data policy.", HttpStatusCode.NotFound => "The account data tables are unavailable. Apply the Krono account schema in Supabase.", _ => $"Account data request failed ({(int)response.StatusCode})." };
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonArray ?? throw new InvalidDataException("Invalid cloud response.");
    }
}
