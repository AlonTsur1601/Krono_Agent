using System.Text.Json;
using System.Text.Json.Nodes;
using Krono_Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

const string Owner = "33333333-3333-4333-8333-333333333333", Other = "44444444-4444-4444-8444-444444444444";
var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
var rows = new Dictionary<(string Account, string Table, string Id), JsonObject>();
var offline = false; var loseNextWriteResponse = false; var raceNextMemoryUpdate = false;
var localFixtureRoot = Path.Combine(Path.GetTempPath(), "krono-sync-" + Guid.NewGuid().ToString("N"));
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
static async Task<JsonObject> Body(HttpContext context) => JsonNode.Parse(await new StreamReader(context.Request.Body).ReadToEndAsync())!.AsObject();
static string Unfilter(string? value) { if (value is null || value.Length < 4) return ""; var text = value[3..]; return text.StartsWith('"') ? JsonSerializer.Deserialize<string>(text)! : text; }
app.MapPost("/auth/v1/token", async (HttpContext context) => {
    var input = await Body(context); var second = input["email"]?.ToString() == "other@fixture.test" || input["refresh_token"]?.ToString() == "test-refresh-other";
    return Results.Json(new { user = new { id = second ? Other : Owner, email = second ? "other@fixture.test" : "owner@fixture.test" }, access_token = second ? "test-access-other" : "test-access-owner", refresh_token = second ? "test-refresh-other" : "test-refresh-owner" });
});
app.MapPost("/auth/v1/logout", () => Results.Json(new { }));
app.MapMethods("/rest/v1/{table}", ["GET", "POST", "PATCH"], Rest);
async Task<IResult> Rest(HttpContext context, string table)
{
    if (offline) return Results.StatusCode(503);
    var account = context.Request.Headers.Authorization.ToString() switch { "Bearer test-access-owner" => Owner, "Bearer test-access-other" => Other, _ => "" };
    if (account.Length == 0 || context.Request.Headers["apikey"] != "fixture-public") return Results.Unauthorized();
    var key = table switch { "account_settings" => "key", "game_saves" => "game_id", "projects" or "chats" or "messages" or "memories" or "game_scores" => "id", _ => "" };
    if (key.Length == 0) return Results.NotFound();
    var query = context.Request.Query;
    if (context.Request.Method != "POST" && Unfilter(query["account_id"]!) != account) return Results.StatusCode(403);
    if (context.Request.Method == "GET") {
        var after = Unfilter(query[key]!); var limit = int.Parse(query["limit"]!);
        return Results.Json(rows.Where(pair => pair.Key.Account == account && pair.Key.Table == table && string.CompareOrdinal(pair.Key.Id, after) > 0).OrderBy(pair => pair.Key.Id, StringComparer.Ordinal).Take(Math.Min(3, limit)).Select(pair => pair.Value).ToArray());
    }
    var payload = await Body(context); var id = payload[key]!.ToString(); var tuple = (account, table, id);
    if (payload["account_id"]?.ToString() != account) return Results.StatusCode(403);
    if (context.Request.Method == "POST") {
        Check(query["on_conflict"] == "account_id," + key && context.Request.Headers["Prefer"].ToString().Contains("ignore-duplicates"), "Insert was not idempotent");
        if (rows.ContainsKey(tuple)) return Results.Json(Array.Empty<JsonObject>());
    } else {
        Check(Unfilter(query[key]!) == id, "PATCH missing record equality filter");
        if (!rows.TryGetValue(tuple, out var current)) return Results.Json(Array.Empty<JsonObject>());
        if (raceNextMemoryUpdate && table == "memories") {
            raceNextMemoryUpdate = false; current["version"] = current["version"]!.GetValue<int>() + 1; current["content"] = "Third device won the race";
        }
        if (Unfilter(query["version"]!) != current["version"]?.ToString()) return Results.Json(Array.Empty<JsonObject>());
        Check(payload["version"]!.GetValue<int>() == current["version"]!.GetValue<int>() + 1, "PATCH did not advance version");
    }
    rows[tuple] = payload;
    if (loseNextWriteResponse && table == "messages") { loseNextWriteResponse = false; return Results.StatusCode(503); }
    return Results.Json(new[] { payload });
}

try {
await app.StartAsync();
Workspace Device(string name) {
    var path = Path.Combine(localFixtureRoot, name); Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "account-service.json"), JsonSerializer.Serialize(new { url = app.Urls.Single(), anonKey = "fixture-public" }));
    return new Workspace(path);
}
static Task<Workspace.SyncSummary> Sync(Workspace workspace) => workspace.Synchronize(CancellationToken.None, () => true);
static string Memory(Workspace workspace) => workspace.Query("SELECT content FROM memories WHERE id='global'").Single()["content"]!.ToString();
static void EditMemory(Workspace workspace, string text) => workspace.Execute("INSERT INTO memories(id,account_id,content,created_at,updated_at) VALUES('global',$p0,$p1,$p2,$p2) ON CONFLICT(id) DO UPDATE SET content=excluded.content,updated_at=excluded.updated_at", workspace.AccountId, text, Workspace.Now);

using var a = Device("device-a"); using var b = Device("device-b"); using var other = Device("device-other");
await a.Login("owner@fixture.test", "fixture-password"); await b.Login("owner@fixture.test", "fixture-password"); await other.Login("other@fixture.test", "fixture-password");
var project = Guid.NewGuid().ToString();
a.Execute("INSERT INTO projects(id,account_id,name,created_at,updated_at) VALUES($p0,$p1,'Sync project',$p2,$p2)", project, Owner, Workspace.Now);
a.Execute("INSERT INTO project_directories(id,project_id,device_id,path) VALUES('fixture-path',$p0,'local','C:\\fixture-private-directory')", project);
a.SetSetting("project." + project, "{\"instructions\":\"Portable instruction\",\"permissions\":\"full\"}");
a.SetSetting("native.theme", "halloween"); a.SetSetting("native.endpoint.custom", "https://fixture-device-endpoint.invalid");
a.SaveKey("openai", "fixture-device-credential");
var chat = a.CreateChat(project); a.AddMessage(chat, "user", "Device A original message");
for (var i = 0; i < 8; i++) a.AddMessage(chat, "assistant", "Pagination reply " + i);
var side = a.CreateSideChat(chat); a.AddMessage(side, "user", "Temporary side stays local");
EditMemory(a, "Shared starting memory");
a.Execute("INSERT INTO game_scores(id,account_id,game_id,score,achieved_at) VALUES('score-one',$p0,'Snake',42,$p1)", Owner, Workspace.Now);
a.Execute("INSERT INTO game_saves(id,account_id,game_id,state,saved_at) VALUES('native-v1:Snake',$p0,'Snake',$p1,$p2)", Owner, "{\"Version\":1,\"Game\":\"Snake\",\"Score\":42}", Workspace.Now);
Check((await Sync(a)).Queued == 0, "First upload left queued rows");
Check((await Sync(b)).Pulled >= 15, "Second device did not pull all tables/pages");
Check(b.Query("SELECT id FROM messages WHERE chat_id=$p0", chat).Count == 9 && Memory(b) == "Shared starting memory", "Shared transcript/memory mismatch");
Check(b.Setting("native.theme") == "halloween" && b.Query("SELECT score FROM game_scores").Single()["score"]?.ToString() == "42" && b.Query("SELECT state FROM game_saves").Count == 1, "Settings/game data did not roundtrip");
Check(b.Query("SELECT id FROM project_directories").Count == 0 && b.Key("openai") == "" && b.Setting("native.endpoint.custom") == "", "A device-only setting escaped");
var config = JsonNode.Parse(b.Setting("project." + project))!;
Check(config["instructions"]?.ToString() == "Portable instruction" && config["permissions"] is null && !rows.ContainsKey((Owner, "chats", side)), "Project permissions or temporary chats escaped");
Check((await Sync(other)).Pulled == 0 && other.Query("SELECT id FROM chats").Count == 0, "Other account received a record");
a.AddMessage(chat, "user", "Parallel append A"); b.AddMessage(chat, "user", "Parallel append B");
await Sync(a); Check((await Sync(b)).Conflicts == 0, "Independent appended messages should not conflict on chat activity time");
await Sync(a); Check(a.Query("SELECT id FROM messages WHERE content LIKE 'Parallel append %'").Count == 2, "Parallel append did not preserve both messages");

offline = true; a.AddMessage(chat, "user", "Offline message");
var queued = await Sync(a); Check(queued.Offline && queued.Queued == 2, "Offline message and chat timestamp were not persisted in the outbox");
using var reopened = Device("device-a"); Check(await reopened.Restore(), "Reopened local session was not restored");
Check(reopened.Query("SELECT id FROM outbox").Count == 2, "Outbox did not survive reopening");
offline = false; loseNextWriteResponse = true; Check((await Sync(reopened)).Offline, "Lost write acknowledgement fixture not reached");
Check((await Sync(reopened)).Queued == 0, "Committed-but-unacknowledged write did not recover");
await Sync(b); Check(b.Query("SELECT id FROM messages WHERE content='Offline message'").Count == 1, "Offline retry lost or duplicated a message");

EditMemory(reopened, "Edit on A"); EditMemory(b, "Edit on B"); await Sync(reopened);
Check((await Sync(b)).Conflicts == 1, "Concurrent edits did not surface a conflict");
b.ResolveSyncConflict(b.SyncConflicts().Single(), true); await Sync(b); await Sync(reopened);
Check(Memory(reopened) == "Edit on B", "Keep local resolution did not converge");
EditMemory(b, "Race attempt"); raceNextMemoryUpdate = true; Check((await Sync(b)).Queued == 1, "CAS rejected write was not retained");
Check((await Sync(b)).Conflicts == 1, "CAS race did not become a conflict");
b.ResolveSyncConflict(b.SyncConflicts().Single(), false); await Sync(b);
Check(Memory(b) == "Third device won the race", "Use cloud did not keep the newer edit");

reopened.Execute("UPDATE chats SET name='Offline rename',updated_at=$p0 WHERE id=$p1", Workspace.Now, chat);
b.Execute("UPDATE chats SET deleted_at=$p0 WHERE id=$p1", Workspace.Now, chat); await Sync(b); await Sync(reopened);
Check(reopened.Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", chat).Count == 0, "Offline rename revived a tombstone");
Check(rows[(Owner,"chats",chat)]["deleted_at"] is not null, "Deletion was not retained in cloud");
Console.WriteLine("NATIVE_SYNC_PASS: two SQLite devices, paginated REST, account isolation, local-only values, durable offline outbox, idempotent retry, CAS conflicts and tombstones");
var now = Workspace.Now;
var schedule = new Workspace.ScheduledTask("fixture-job", "Repeat test", "Fixture prompt", "openai", "fixture-model", "chat", "ask", null, "interval", TimeZoneInfo.Utc.Id, 5, 0, now - 20*60000, "active");
a.SaveTask(schedule);
Check(a.ClaimScheduledRun(now) is null, "Scheduler ran without device opt-in");
a.SetSetting("native.scheduleEnabled", "true"); a.SetTaskState(schedule.Id, "paused");
Check(a.ClaimScheduledRun(now) is null, "Paused schedule ran");
a.SetTaskState(schedule.Id, "active");
var claims = await Task.WhenAll(Task.Run(() => a.ClaimScheduledRun(now)), Task.Run(() => reopened.ClaimScheduledRun(now)));
Check(claims.Count(run => run is not null) == 1 && a.ScheduledHistory().Count == 1, "Concurrent schedule claims duplicated an occurrence");
a.FinishScheduledRun(claims.Single(run => run is not null)!.Id, "completed");
Check(a.ScheduledTasks().Single().NextRun == now + 5*60000 && a.ClaimScheduledRun(now) is null, "Missed intervals produced more than one catch-up");
var once = schedule with { Id = "fixture-once", Recurrence = "once", NextRun = now - 1 };
a.SaveTask(once); var one = a.ClaimScheduledRun(now); Check(one?.Task.Id == once.Id, "One-time task was not claimed");
a.FinishScheduledRun(one!.Id, "completed"); Check(a.ClaimScheduledRun(now) is null, "One-time task repeated");
a.SetTaskState(schedule.Id, "cancelled"); Check(a.ClaimScheduledRun(now + 10*60000) is null, "Cancelled task ran");
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
var gap = Workspace.ScheduleTime(new DateTime(2026,3,8,2,30,0), eastern);
Check(TimeZoneInfo.ConvertTime(gap, eastern).Hour == 3 && TimeZoneInfo.ConvertTime(gap, eastern).Minute == 0, "DST gap did not advance to valid time");
var daily = schedule with { Recurrence = "daily", TimeZone = eastern.Id, ClockMinutes = 9*60 };
var anchor = new DateTimeOffset(2026,11,1,15,0,0,TimeSpan.Zero).ToUnixTimeMilliseconds();
var next = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(Workspace.NextScheduledTime(daily, anchor)), eastern);
Check(next.Day == 2 && next.Hour == 9, "Daily schedule drifted across timezone change");
Console.WriteLine("NATIVE_SCHEDULE_PASS: opt-in, pause/cancel, one-time, concurrent claim, single catch-up, timezone and DST");
var textFile = Path.Combine(localFixtureRoot, "fixture-source.txt");
await File.WriteAllTextAsync(textFile, "שלום\n```\nreference\n```", System.Text.Encoding.Unicode);
var attachment = await TextAttachment.Read(textFile); var composed = TextAttachment.Compose("Read this", [attachment]);
Check(attachment.Name == "fixture-source.txt" && composed.Contains("שלום") && composed.Contains("\n~~~\nשלום") && !composed.Contains(localFixtureRoot), "Attachment encoding, fences or basename-only snapshot");
await File.WriteAllBytesAsync(textFile, new byte[TextAttachment.MaxBytes + 1]);
try { _ = await TextAttachment.Read(textFile); throw new Exception("Oversized attachment accepted"); } catch (InvalidOperationException) { }
await File.WriteAllBytesAsync(textFile, [0, 1, 2]);
try { _ = await TextAttachment.Read(textFile); throw new Exception("Binary attachment accepted"); } catch (InvalidOperationException) { }
Console.WriteLine("NATIVE_ATTACHMENT_PASS: Unicode text snapshot, bounded size, safe fences and binary rejection");
return 0;
} catch (Exception error) {
    Console.Error.WriteLine(error.Message); return 1;
}
