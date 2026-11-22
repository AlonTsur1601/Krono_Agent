namespace Krono_Agent;

static class PluginCatalog
{
    internal sealed record Plugin(string Id, string Name, string Description, bool NeedsToken = false, string? SetupUrl = null)
    {
        public string Setting => "native.plugin." + Id;
    }

    internal static readonly Plugin[] All = [
        new("github", "GitHub", "Read repositories, issues and pull requests. Use a fine-grained token with access to the selected repositories and read permissions for Issues and Pull requests.", true, "https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens"),
        new("vercel", "Vercel", "Read projects and deployment status. Use a Vercel access token scoped to the relevant account or team.", true, "https://vercel.com/docs/rest-api"),
        new("firebase", "Firebase", "Read Firebase projects and registered apps. On this computer, sign into Google Cloud CLI with gcloud auth application-default login, then test the connection.", SetupUrl: "https://cloud.google.com/docs/authentication/set-up-adc-local-dev-environment"),
        new("node", "Node.js", "Inspect package.json, engine requirements, dependency names and available npm scripts in your project."),
    ];

    internal const string TeamSetting = "native.plugin.vercel.team";

    internal static object? Configuration(Workspace workspace, Plugin plugin)
    {
        if (!plugin.NeedsToken) return new { id = plugin.Id };
        var token = workspace.Key(plugin.Setting);
        if (token.Length == 0) return null;
        var teamId = plugin.Id == "vercel" ? workspace.Setting(TeamSetting) : "";
        if (teamId.Length > 0) return new { id = plugin.Id, token, teamId };
        return new { id = plugin.Id, token };
    }

    internal static object[] Enabled(Workspace workspace) => All
        .Where(plugin => workspace.Setting(plugin.Setting) == "true")
        .Select(plugin => Configuration(workspace, plugin))
        .OfType<object>()
        .ToArray();
}
