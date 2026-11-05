using System.Text.Json.Nodes;

namespace Krono_Agent;

// The same public profile data is bundled by the worker and native interface.
public static class ProviderCatalog
{
    static readonly JsonObject Profiles = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Engine", "providers.json")))!.AsObject();
    static readonly Dictionary<string, string> Direct = new() { ["openai"] = "OpenAI", ["anthropic"] = "Anthropic", ["google"] = "Google" };
    public static string[] Ids => Direct.Keys.Concat(Profiles.Select(profile => profile.Key)).ToArray();
    public static bool Compatible(string id) => Profiles.ContainsKey(id);
    public static bool RequiresKey(string id) => !Compatible(id) || Profiles[id]?["requiresKey"]?.GetValue<bool>() == true;
    public static bool Local(Uri uri) => uri.Host is "localhost" or "127.0.0.1" or "[::1]";
    public static Uri ParseEndpoint(string value) {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && Local(uri))) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidOperationException("Use HTTPS, or HTTP on loopback only. Do not put credentials or query parameters in the URL.");
        return uri;
    }
    public static string Name(string id) => Direct.TryGetValue(id, out var name) ? name : Profiles[id]?["name"]?.ToString() ?? id;
    public static string Endpoint(string id) => Compatible(id) ? Profiles[id]?["endpoint"]?.ToString() ?? "" : "";
    public static string Help(string id) => Compatible(id) ? Profiles[id]?["help"]?.ToString() ?? "" : "Enter your provider's API key, load its current models or enter an exact model ID, then choose Use model. Usage follows your provider account.";
}
