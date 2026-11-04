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

public sealed class Workspace : IDisposable
{
    public static readonly string Home = Environment.GetEnvironmentVariable("KRONO_USER_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "krono-agent");
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string url = "", key = "", access = "", refresh = "";
    public string AccountId { get; private set; } = "";
    public string Email { get; private set; } = "";
    public bool IsOfflineSession { get; private set; }
    private SqliteConnection? db;
    public Workspace() { Directory.CreateDirectory(Home); }
    private static string SessionFile => Path.Combine(Home, "native-session.bin");
    public static void Protect(string file, string value) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); var temporary = file + "." + Guid.NewGuid() + ".tmp"; File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); File.Move(temporary, file, true); }
    public static string? Unprotect(string file) { if (!File.Exists(file)) return null; return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)); }
    private void Configure()
    {
        var configFile = Path.Combine(Home, "account-service.json");
        var config = File.Exists(configFile) ? JsonNode.Parse(File.ReadAllText(configFile))! : new JsonObject();
        url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? config["url"]?.GetValue<string>() ?? throw new InvalidOperationException("Create account-service.json in %APPDATA%/krono-agent with your Supabase project URL and publishable key.");
        key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? config["anonKey"]?.GetValue<string>() ?? throw new InvalidOperationException("Add the publishable key to account-service.json.");
        var uri = new Uri(url);
        if ((uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")) || !string.IsNullOrEmpty(uri.UserInfo) || key.StartsWith("sb_secret_")) throw new InvalidOperationException("Use an HTTPS project URL and a publishable key in account-service.json.");
        if (key.Split('.').Length == 3) { try { var token = key.Split('.')[1].Replace('-', '+').Replace('_', '/'); token = token.PadRight((token.Length + 3) / 4 * 4, '='); if (JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(token)))?["role"]?.ToString() == "service_role") throw new InvalidOperationException("Administrative keys cannot be used in the desktop application."); } catch (FormatException) { throw new InvalidOperationException("Invalid public key."); } }
    }
    private async Task<JsonNode> Auth(string endpoint, object? body = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url.TrimEnd('/') + "/auth/v1/" + endpoint);
        request.Headers.Add("apikey", key);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var result = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(result["msg"]?.ToString() ?? result["message"]?.ToString() ?? result["error_description"]?.ToString() ?? "Account request failed.");
        return result;
    }
    public async Task Login(string email, string password, bool register = false)
    {
        Configure();
        if (password.Length < 8) throw new InvalidOperationException("Use a password of at least eight characters.");
        var endpoint = register ? "signup?redirect_to=" + Uri.EscapeDataString("https://alontsur1601.github.io/Krono_Agent/") : "token?grant_type=password";
        var result = await Auth(endpoint, new { email, password });
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
        refresh = saved["refresh_token"]!.GetValue<string>();
        try { Accept(await Auth("token?grant_type=refresh_token", new { refresh_token = refresh })); return true; }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { IsOfflineSession = true; Open(saved["user"]!); return true; }
        catch { File.Delete(SessionFile); return false; }
    }
    public async Task Logout(bool allDevices = false)
    {
        try { if (access.Length > 0) await Auth("logout?scope=" + (allDevices ? "global" : "local"), new { }, access); }
        finally { if (File.Exists(SessionFile)) File.Delete(SessionFile); access = refresh = ""; db?.Dispose(); db = null; AccountId = Email = ""; }
    }
    private void Open(JsonNode user)
    {
        AccountId = Guid.Parse(user["id"]!.GetValue<string>()).ToString(); Email = user["email"]!.GetValue<string>();
        db?.Dispose(); Directory.CreateDirectory(Path.Combine(Home, "databases"));
        db = new SqliteConnection($"Data Source={Path.Combine(Home, "databases", AccountId + ".db")}"); db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Engine", "schema.sql")));
        Execute("INSERT INTO accounts(id,email,created_at) VALUES($p0,$p1,$p2) ON CONFLICT(id) DO UPDATE SET email=excluded.email", AccountId, Email, Now);
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
    public void SaveKey(string provider, string secret) => Protect(Path.Combine(Home, "native-credentials", AccountId, provider + ".bin"), secret);
    public string Key(string provider) => Unprotect(Path.Combine(Home, "native-credentials", AccountId, provider + ".bin")) ?? "";
    public string CreateChat(string? project = null) { var id = Guid.NewGuid().ToString("N"); Execute("INSERT INTO chats(id,account_id,project_id,name,created_at,updated_at) VALUES($p0,$p1,$p2,'New chat',$p3,$p3)", id, AccountId, project, Now); return id; }
    public void AddMessage(string chat, string role, string content) { Execute("INSERT INTO messages(id,chat_id,role,content,created_at) VALUES($p0,$p1,$p2,$p3,$p4)", Guid.NewGuid().ToString("N"), chat, role, content, Now); Execute("UPDATE chats SET updated_at=$p0 WHERE id=$p1", Now, chat); }
    public void Dispose() { db?.Dispose(); http.Dispose(); }
}

public sealed class AgentWorker : IDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pending = new();
    public event Action<string, string, JsonNode?>? Event;
    private readonly SemaphoreSlim writer = new(1);
    public AgentWorker()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Engine");
        process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(folder, "node.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder } };
        process.StartInfo.ArgumentList.Add(Path.Combine(folder, "engine.cjs"));
        process.Start();
        _ = Task.Run(async () => { try { while (await process.StandardOutput.ReadLineAsync() is { } line) { var message = JsonNode.Parse(line)!; var id = message["id"]!.ToString(); if (message["event"] is { } type) Event?.Invoke(id, type.ToString(), message["data"]); else if (pending.TryRemove(id, out var waiter)) { if (message["error"] is { } error) waiter.TrySetException(new InvalidOperationException(error.ToString())); else waiter.TrySetResult(message["result"]); } } } catch (Exception error) { foreach (var waiter in pending.Values) waiter.TrySetException(error); } finally { foreach (var waiter in pending.Values) waiter.TrySetException(new IOException("Agent worker stopped.")); } });
        _ = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is not null) { /* SDK diagnostics may contain request data: never forward to UI logs. */ } });
    }
    public async Task Send(string id, string method, object data) { await writer.WaitAsync(); try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, data })); await process.StandardInput.FlushAsync(); } finally { writer.Release(); } }
    public async Task<JsonNode?> Request(string id, string method, object data) { var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = waiter; try { await Send(id, method, data); return await waiter.Task; } finally { pending.TryRemove(id, out _); } }
    public void Dispose() { try { process.StandardInput.Close(); if (!process.WaitForExit(1500)) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); }
}
