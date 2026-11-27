using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed partial class Workspace
{
    public string? GoogleEmail { get; private set; }

    static string? ReadGoogleEmail(JsonNode user)
    {
        var google = (user["identities"] as JsonArray)?.FirstOrDefault(identity => identity?["provider"]?.ToString() == "google");
        return google is null ? null : google["identity_data"]?["email"]?.ToString() ?? user["email"]?.ToString();
    }

    public async Task GoogleAuthenticate(bool link, CancellationToken cancellation)
    {
        Configure();
        var account = AccountId;
        if (link && (account.Length == 0 || IsOfflineSession))
            throw new InvalidOperationException("Connect to the account service before linking Google.");

        var epoch = ++sessionEpoch;
        var settings = await Auth("settings");
        cancellation.ThrowIfCancellationRequested();
        if (settings["external"]?["google"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Google sign-in has not been enabled for this Krono account service.");
        if (link && settings["manual_linking_enabled"]?.GetValue<bool>() == false)
            throw new InvalidOperationException("Google account linking has not been enabled for this Krono account service.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var listener = new HttpListener();
        var callbackPath = "/krono-auth/" + Guid.NewGuid().ToString("N") + "/";
        string callback;
        for (var attempt = 0; ; attempt++) {
            var portSelector = new TcpListener(IPAddress.Loopback, 0);
            portSelector.Start();
            var port = ((IPEndPoint)portSelector.LocalEndpoint).Port;
            portSelector.Stop();
            callback = $"http://127.0.0.1:{port}{callbackPath}";
            listener.Prefixes.Add(callback);
            try { listener.Start(); break; }
            catch (HttpListenerException) when (attempt < 2) { listener.Prefixes.Clear(); }
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = $"provider=google&redirect_to={Uri.EscapeDataString(callback)}&code_challenge={challenge}&code_challenge_method=s256&prompt=select_account";
        var destination = url.TrimEnd('/') + "/auth/v1/authorize?" + query;
        if (link) {
            var current = await Auth("token?grant_type=refresh_token", new { refresh_token = refresh });
            if (epoch != sessionEpoch || current["user"]?["id"]?.ToString() != account)
                throw new OperationCanceledException();
            SaveSession(current);
            var authorization = await Auth("user/identities/authorize?" + query + "&skip_http_redirect=true", bearer: access);
            destination = authorization["url"]?.ToString() ?? "";
        }
        var service = new Uri(url);
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var target) || target.UserInfo.Length > 0 ||
            (target.GetLeftPart(UriPartial.Authority) != service.GetLeftPart(UriPartial.Authority) &&
             !(target.Scheme == "https" && target.Host == "accounts.google.com" && target.IsDefaultPort)))
            throw new InvalidOperationException("The account service returned an invalid Google sign-in URL.");

        deadline.Token.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        var (session, context) = await WaitForGoogleSession(listener, callbackPath, verifier, link ? account : null, deadline.Token, cancellation);
        using var response = context.Response;
        try {
            deadline.Token.ThrowIfCancellationRequested();
            if (epoch != sessionEpoch) throw new OperationCanceledException();
            if (link) {
                SaveSession(session);
                GoogleEmail = ReadGoogleEmail(session["user"]!);
            } else Accept(session);
        }
        catch { await Reply(response, false); throw; }
        await Reply(response, true);
    }

    async Task<(JsonNode Session, HttpListenerContext Context)> WaitForGoogleSession(
        HttpListener listener, string callbackPath, string verifier, string? account,
        CancellationToken deadline, CancellationToken cancellation)
    {
        while (true) {
            var (code, context) = await ReadGoogleCallback(listener, callbackPath, deadline, cancellation);
            JsonNode? session;
            try {
                session = await Auth("token?grant_type=pkce", new { auth_code = code, code_verifier = verifier });
            } catch (InvalidOperationException) { session = null; }
            if (session?["user"] is { } user && ReadGoogleEmail(user) is not null &&
                (account is null || user["id"]?.ToString() == account))
                return (session, context);
            using var rejected = context.Response;
            await Reply(rejected, false);
        }
    }

    static async Task<(string Code, HttpListenerContext Context)> ReadGoogleCallback(
        HttpListener listener, string callbackPath, CancellationToken deadline, CancellationToken cancellation)
    {
        while (true) {
            deadline.ThrowIfCancellationRequested();
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(deadline); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) {
                throw new TimeoutException("Google sign-in timed out. Start it again in Krono.");
            }

            var request = context.Request;
            var codes = request.QueryString.GetValues("code");
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == callbackPath &&
                codes is [string code] && code.Length is > 0 and <= 2048)
                return (code, context);
            using var rejected = context.Response;
            await Reply(rejected, false);
        }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static async Task Reply(HttpListenerResponse response, bool complete)
    {
        var title = complete ? "Google account verified" : "Google authorization did not complete";
        var message = complete
            ? "You can close this tab and return to Krono Agent."
            : "Return to Krono Agent to try again.";
        var markFile = Path.Combine(AppContext.BaseDirectory, "Assets", "Krono.svg");
        var mark = Convert.ToBase64String(await File.ReadAllBytesAsync(markFile));
        var body = Encoding.UTF8.GetBytes($$"""
            <!doctype html>
            <html lang="en">
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{title}} · Krono Agent</title>
            <style>
              body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #10151d; color: #f4f8f6; font: 16px/1.55 system-ui, sans-serif; }
              main { margin: 24px; width: min(400px, calc(100% - 112px)); padding: 32px; border: 1px solid #344452; border-radius: 22px; background: #1b2532; }
              img { display: block; } h1 { line-height: 1.15; } p { color: #d2dce5; margin-bottom: 0; }
            </style>
            <main>
              <img src="data:image/svg+xml;base64,{{mark}}" width="48" height="48" alt="Krono Agent">
              <h1>{{title}}</h1>
              <p>{{message}}</p>
            </main>
            </html>
            """);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = body.Length;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'none'";
        try { await response.OutputStream.WriteAsync(body); }
        catch (Exception error) when (error is IOException or HttpListenerException) { }
    }
}
