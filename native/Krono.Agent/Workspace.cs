using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace Krono_Agent;

public sealed partial class Workspace : IDisposable
{
    public static readonly string Home = Environment.GetEnvironmentVariable("KRONO_USER_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "krono-agent");
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private string url = "", key = "", access = "", refresh = "";
    public string AccountId { get; private set; } = "";
    public string Email { get; private set; } = "";
    public bool IsOfflineSession { get; private set; }
    private SqliteConnection? db;
    readonly string dataHome;
    int sessionEpoch;
    public Workspace(string? dataDirectory = null) { dataHome = dataDirectory ?? Home; Directory.CreateDirectory(dataHome); }
    private string SessionFile => Path.Combine(dataHome, "native-session.bin");
    public static void Protect(string file, string value) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); var temporary = file + "." + Guid.NewGuid() + ".tmp"; File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); File.Move(temporary, file, true); }
    public static string? Unprotect(string file) { if (!File.Exists(file)) return null; return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)); }
    private void Configure()
    {
        var configFile = Path.Combine(dataHome, "account-service.json");
        var config = File.Exists(configFile) ? JsonNode.Parse(File.ReadAllText(configFile))! : new JsonObject();
        url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? config["url"]?.GetValue<string>() ?? throw new InvalidOperationException("Create account-service.json in %APPDATA%/krono-agent with your Supabase project URL and publishable key.");
        key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? config["anonKey"]?.GetValue<string>() ?? throw new InvalidOperationException("Add the publishable key to account-service.json.");
        var uri = new Uri(url);
        if ((uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0 || key.StartsWith("sb_secret_")) throw new InvalidOperationException("Use an HTTPS project URL and a publishable key in account-service.json.");
        if (key.Split('.').Length == 3) { try { var token = key.Split('.')[1].Replace('-', '+').Replace('_', '/'); token = token.PadRight((token.Length + 3) / 4 * 4, '='); if (JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(token)))?["role"]?.ToString() == "service_role") throw new InvalidOperationException("Administrative keys cannot be used in the desktop application."); } catch (FormatException) { throw new InvalidOperationException("Invalid public key."); } }
    }
    private async Task<JsonNode> Auth(string endpoint, object? body = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url.TrimEnd('/') + "/auth/v1/" + endpoint);
        request.Headers.Add("apikey", key);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429) throw new HttpRequestException("Account service temporarily unavailable.", null, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var result = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(result["msg"]?.ToString() ?? result["message"]?.ToString() ?? result["error_description"]?.ToString() ?? "Account request failed.");
        return result;
    }
    public async Task Login(string email, string password, bool register = false)
    {
        Configure();
        var epoch = ++sessionEpoch;
        if (password.Length < 8) throw new InvalidOperationException("Use a password of at least eight characters.");
        var endpoint = register ? "signup?redirect_to=" + Uri.EscapeDataString("https://alontsur1601.github.io/Krono_Agent/") : "token?grant_type=password";
        var result = await Auth(endpoint, new { email, password });
        if (epoch != sessionEpoch) throw new OperationCanceledException();
        if (result["access_token"] is null) throw new InvalidOperationException("Check your email to confirm the account, then sign in.");
        Accept(result);
    }
    private void Accept(JsonNode session)
    {
        IsOfflineSession = false;
        access = session["access_token"]!.GetValue<string>(); refresh = session["refresh_token"]!.GetValue<string>();
        Protect(SessionFile, session.ToJsonString()); Open(session["user"]!);
    }
    public async Task<bool> Restore()
    {
        if (!File.Exists(SessionFile)) return false;
        Configure();
        var saved = JsonNode.Parse(Unprotect(SessionFile)!)!;
        var epoch = sessionEpoch;
        refresh = saved["refresh_token"]!.GetValue<string>();
        try { var renewed = await Auth("token?grant_type=refresh_token", new { refresh_token = refresh }); if (epoch != sessionEpoch) return false; Accept(renewed); return true; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { if (epoch != sessionEpoch) return false; IsOfflineSession = true; Open(saved["user"]!); return true; }
        catch { if (epoch == sessionEpoch) File.Delete(SessionFile); return false; }
    }
    public async Task Logout(bool allDevices = false)
    {
        sessionEpoch++;
        try { if (access.Length > 0) await Auth("logout?scope=" + (allDevices ? "global" : "local"), new { }, access); }
        finally { if (File.Exists(SessionFile)) File.Delete(SessionFile); access = refresh = ""; db?.Dispose(); db = null; AccountId = Email = ""; }
    }
    private void Open(JsonNode user)
    {
        AccountId = Guid.Parse(user["id"]!.GetValue<string>()).ToString(); Email = user["email"]!.GetValue<string>();
        db?.Dispose(); Directory.CreateDirectory(Path.Combine(dataHome, "databases"));
        db = new SqliteConnection($"Data Source={Path.Combine(dataHome, "databases", AccountId + ".db")}"); db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Engine", "schema.sql")));
        Execute("INSERT INTO accounts(id,email,created_at) VALUES($p0,$p1,$p2) ON CONFLICT(id) DO UPDATE SET email=excluded.email", AccountId, Email, Now);
        PurgeExpiredSideChats();
    }
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Execute(string sql, params object?[] args) { using var command = Command(sql, args); command.ExecuteNonQuery(); }
    private SqliteCommand Command(string sql, object?[] args) { var command = (db ?? throw new InvalidOperationException("Sign in first.")).CreateCommand(); command.CommandText = sql; for (var i = 0; i < args.Length; i++) command.Parameters.AddWithValue("$p" + i, args[i] ?? DBNull.Value); return command; }
    public List<JsonObject> Query(string sql, params object?[] args)
    {
        using var command = Command(sql, args); using var rows = command.ExecuteReader(); var result = new List<JsonObject>();
        while (rows.Read()) { var row = new JsonObject(); for (var i = 0; i < rows.FieldCount; i++) row[rows.GetName(i)] = rows.IsDBNull(i) ? null : JsonSerializer.SerializeToNode(rows.GetValue(i)); result.Add(row); } return result;
    }
    public string Setting(string name) => Query("SELECT value FROM settings WHERE key=$p0", name).FirstOrDefault()?["value"]?.ToString() ?? "";
    public void SetSetting(string name, string value) => Execute("INSERT INTO settings(key,value) VALUES($p0,$p1) ON CONFLICT(key) DO UPDATE SET value=excluded.value", name, value);
    public JsonObject ProjectPreferences(string id) { var value = Setting("project." + id); if (value.Length == 0) return new(); try { return JsonNode.Parse(value) as JsonObject ?? new(); } catch (JsonException) { return new(); } }
    public void SaveKey(string provider, string secret) => Protect(Path.Combine(dataHome, "native-credentials", AccountId, provider + ".bin"), secret);
    public string Key(string provider) => Unprotect(Path.Combine(dataHome, "native-credentials", AccountId, provider + ".bin")) ?? "";
    public string CreateChat(string? project = null) { var id = Guid.NewGuid().ToString("N"); Execute("INSERT INTO chats(id,account_id,project_id,name,created_at,updated_at) VALUES($p0,$p1,$p2,'New chat',$p3,$p3)", id, AccountId, project, Now); return id; }
    public void AddMessage(string chat, string role, string content) {
        if (Query("SELECT id FROM chats WHERE id=$p0 AND deleted_at IS NULL", chat).Count == 0 || Query("SELECT chat_id FROM side_chats WHERE chat_id=$p0 AND expires_at<=$p1", chat, Now).Count > 0) throw new InvalidOperationException("This conversation is no longer available.");
        Execute("INSERT INTO messages(id,chat_id,role,content,created_at) VALUES($p0,$p1,$p2,$p3,$p4)", Guid.NewGuid().ToString("N"), chat, role, content, Now); Execute("UPDATE chats SET updated_at=$p0 WHERE id=$p1", Now, chat);
        if (role is "user" or "assistant") Execute("UPDATE side_chats SET expires_at=$p0 WHERE chat_id=$p1", Now + SideLifetime, chat);
    }
    public void Dispose() { db?.Dispose(); http.Dispose(); }
}
