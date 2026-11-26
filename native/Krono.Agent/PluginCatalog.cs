using System.Text.Json;
using System.Text.Json.Nodes;

namespace Krono_Agent;

static class PluginCatalog
{
    internal sealed record Plugin(string Id, string Name, string Type, string Icon, string Color, string Description, string? SetupUrl)
    {
        internal string Setting => "native.plugin." + Id;
        internal string GrantSetting => Setting + ".grant";
        internal bool RequiresAccount => Type == "account";
        internal bool UsesCli => Id is "github" or "firebase" or "vercel";
    }

    internal static readonly Plugin[] All = Load();

    static Plugin[] Load()
    {
        using var manifest = typeof(PluginCatalog).Assembly.GetManifestResourceStream("KronoAgent.PluginCatalog.json")
            ?? throw new InvalidOperationException("The plugin catalog is missing.");
        return JsonSerializer.Deserialize<Plugin[]>(manifest, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    static string Packaged(Plugin plugin) => Path.Combine(AppContext.BaseDirectory, "Engine", "plugin-packages", plugin.Id + ".json");

    static JsonObject? ReadInstalled(Workspace workspace, Plugin plugin)
    {
        if (workspace.AccountId.Length == 0 || workspace.Setting(plugin.Setting) == "false") return null;
        var path = workspace.InstalledPluginPath(plugin.Id);
        try {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > 16_384) return null;
            var contents = File.ReadAllBytes(path);
            if (!contents.AsSpan().SequenceEqual(File.ReadAllBytes(Packaged(plugin)))) return null;
            return JsonNode.Parse(contents) as JsonObject;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) {
            return null;
        }
    }

    internal static bool Installed(Workspace workspace, Plugin plugin) => ReadInstalled(workspace, plugin) is not null;

    internal static void Install(Workspace workspace, Plugin plugin)
    {
        if (workspace.AccountId.Length == 0) return;
        var contents = File.ReadAllBytes(Packaged(plugin));
        var package = JsonNode.Parse(contents);
        if (contents.Length > 16_384 || package?["id"]?.ToString() != plugin.Id || package?["type"]?.ToString() != plugin.Type)
            throw new InvalidOperationException("The plugin package is invalid.");
        var path = workspace.InstalledPluginPath(plugin.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllBytes(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        } finally { File.Delete(temporary); }
        workspace.SetSetting(plugin.Setting, "true");
    }

    internal static void RestoreInstallations(Workspace workspace)
    {
        foreach (var plugin in All) {
            if (workspace.Setting(plugin.Setting) != "true" || Installed(workspace, plugin)) continue;
            try { Install(workspace, plugin); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
                App.RecordFault("Plugin package restoration", error);
            }
        }
    }

    internal static JsonObject? Configuration(Workspace workspace, Plugin plugin)
    {
        if (ReadInstalled(workspace, plugin) is not { } package) return null;
        if (!plugin.RequiresAccount) {
            var published = new JsonObject { ["id"] = package["id"]?.DeepClone() };
            if (plugin.Type == "skill") {
                published["skill"] = true;
                published["guidance"] = package["guidance"]?.DeepClone();
            }
            else published["tools"] = package["tools"]?.DeepClone();
            return published;
        }
        var value = workspace.Key(plugin.GrantSetting);
        if (value.Length > 0) {
            try {
                if (JsonNode.Parse(value) is not JsonObject configuration) return null;
                configuration["tools"] = package["tools"]?.DeepClone();
                return configuration;
            } catch (JsonException) { return null; }
        }

        // Previously configured, encrypted credentials still work until the user reconnects or removes the plugin.
        if (plugin.Id is not ("github" or "vercel") || workspace.Key(plugin.Setting) is not { Length: > 0 } credential) return null;
        var config = new JsonObject { ["id"] = plugin.Id, ["token"] = credential, ["tools"] = package["tools"]?.DeepClone() };
        if (plugin.Id == "vercel" && workspace.Setting("native.plugin.vercel.team") is { Length: > 0 } team)
            config["teamId"] = team;
        return config;
    }

    internal static object[] Enabled(Workspace workspace, bool includeTools = true) => All
        .Where(plugin => includeTools || plugin.Type == "skill")
        .Select(plugin => Configuration(workspace, plugin)).OfType<object>().ToArray();

    internal static void SaveConnection(Workspace workspace, Plugin plugin, JsonNode response, string? profile)
    {
        if (!Installed(workspace, plugin)) return;
        var previous = Configuration(workspace, plugin)?["profile"]?.GetValue<string>();
        var config = new JsonObject { ["id"] = plugin.Id, ["grantId"] = Guid.NewGuid().ToString() };
        if (plugin.UsesCli && profile is not null && response["profile"]?.ToString() == profile) config["profile"] = profile;
        else if (!plugin.UsesCli && response["connection"] is JsonObject connection) config["connection"] = connection.DeepClone();
        else throw new InvalidOperationException("The provider did not return a valid sign-in session.");
        workspace.SaveKey(plugin.GrantSetting, config.ToJsonString());
        if (plugin.Id is "github" or "vercel") workspace.SaveKey(plugin.Setting, "");
        if (previous is not null && previous != profile) workspace.ClearPluginProfile(plugin.Id, previous);
    }

    internal static void AcceptRefresh(Workspace workspace, JsonNode data)
    {
        if (workspace.AccountId.Length == 0 || data["connection"] is not JsonObject connection) return;
        var plugin = All.FirstOrDefault(item => item.Id == data["pluginId"]?.ToString());
        if (plugin is null || !Installed(workspace, plugin)) return;
        var config = Configuration(workspace, plugin);
        if (config is null || config["grantId"] is not { } current || current.ToString() != data["grantId"]?.ToString()) return;
        config["connection"] = connection.DeepClone();
        workspace.SaveKey(plugin.GrantSetting, config.ToJsonString());
    }

    internal static void Remove(Workspace workspace, Plugin plugin)
    {
        var profile = plugin.UsesCli ? Configuration(workspace, plugin)?["profile"]?.GetValue<string>() : null;
        workspace.SetSetting(plugin.Setting, "false");
        File.Delete(workspace.InstalledPluginPath(plugin.Id));
        if (plugin.RequiresAccount) workspace.SaveKey(plugin.GrantSetting, "");
        if (plugin.Id is "github" or "vercel") workspace.SaveKey(plugin.Setting, "");
        if (profile is not null) workspace.ClearPluginProfile(plugin.Id, profile);
    }
}

public sealed partial class Workspace
{
    internal string InstalledPluginPath(string plugin) => Path.Combine(dataHome, "native-installed-plugins", AccountId, plugin + ".json");

    string PluginProfileRoot(string plugin) => Path.Combine(dataHome, "native-plugin-profiles", AccountId, plugin);
    internal string NewPluginProfile(string plugin) => Path.GetFullPath(Path.Combine(PluginProfileRoot(plugin), Guid.NewGuid().ToString("N")));

    internal void ClearPluginProfile(string plugin, string path)
    {
        try {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(PluginProfileRoot(plugin));
            if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _) || !Directory.Exists(full)) return;
            Directory.Delete(full, recursive: true);
        } catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { }
    }
}
