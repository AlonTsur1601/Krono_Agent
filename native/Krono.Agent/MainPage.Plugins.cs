using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    void AddPluginSettings(StackPanel panel)
    {
        panel.Children.Add(new TextBlock {
            Text = "Enable the tools you want in Agent and Goal modes. Account tools ask before accessing a cloud service when permissions are set to Ask.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = .75,
        });
        foreach (var plugin in PluginCatalog.All) {
            var content = new StackPanel { Spacing = 10, Padding = new Thickness(0, 2, 0, 6) };
            var expander = KronoDesign.Section(plugin.Name, content);
            AutomationProperties.SetAutomationId(expander, "Plugin-" + plugin.Id);
            panel.Children.Add(expander);
            content.Children.Add(new TextBlock { Text = plugin.Description, TextWrapping = TextWrapping.Wrap });
            if (plugin.SetupUrl is not null) content.Children.Add(new HyperlinkButton { Content = "Setup guide", NavigateUri = new Uri(plugin.SetupUrl) });

            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .8 };
            AutomationProperties.SetAutomationId(status, "PluginStatus-" + plugin.Id);
            void ConnectionError(string message)
            {
                status.Text = "";
                Error(new InvalidOperationException(message));
            }
            var enabled = new KronoCheckBox { Content = "Enable " + plugin.Name, IsChecked = workspace.Setting(plugin.Setting) == "true" };
            AutomationProperties.SetAutomationId(enabled, "PluginEnabled-" + plugin.Id);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button? clearToken = null;
            KronoTextBox? team = null;
            if (plugin.NeedsToken) {
                var token = new PasswordBox { Header = plugin.Name + " access token", HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 4096 };
                KronoInputMotion.Attach(token, "KronoPasswordInput");
                AutomationProperties.SetAutomationId(token, "PluginToken-" + plugin.Id);
                content.Children.Add(token);
                if (plugin.Id == "vercel") {
                    team = new KronoTextBox { Header = "Team ID (optional)", Text = workspace.Setting(PluginCatalog.TeamSetting), MaxLength = 100 };
                    content.Children.Add(team);
                }
                void SaveConnection()
                {
                    var credential = token.Password.Trim();
                    var teamId = team?.Text.Trim() ?? "";
                    if (teamId.Length > 0 && !Regex.IsMatch(teamId, "^team_[a-zA-Z0-9_]+$")) { ConnectionError("Enter a Vercel team ID that starts with team_."); return; }
                    if (credential.Length == 0 && workspace.Key(plugin.Setting).Length == 0) { ConnectionError("Enter an access token first."); return; }
                    if (credential.Any(character => character is < '!' or > '~')) { ConnectionError("The access token contains an invalid character."); return; }
                    if (credential.Length > 0) workspace.SaveKey(plugin.Setting, credential);
                    token.Password = "";
                    if (team is not null) workspace.SetSetting(PluginCatalog.TeamSetting, teamId);
                    if (clearToken is not null) clearToken.IsEnabled = true;
                    status.Text = "Connection settings saved.";
                }
                actions.Children.Add(Button("Save connection", SaveConnection));
                clearToken = Button("Remove token", () => {
                    workspace.SaveKey(plugin.Setting, "");
                    enabled.IsChecked = false;
                    if (clearToken is not null) clearToken.IsEnabled = false;
                    token.Password = "";
                    status.Text = "Access token removed.";
                });
                clearToken.IsEnabled = workspace.Key(plugin.Setting).Length > 0;
                if (clearToken.IsEnabled) status.Text = "An access token is saved on this computer.";
                actions.Children.Add(clearToken);
            }

            Button? test = null;
            test = Button(plugin.NeedsToken || plugin.Id == "firebase" ? "Test connection" : "Check runtime", async () => {
                if (test is not { IsEnabled: true } || worker is not { } active) return;
                var configuration = PluginCatalog.Configuration(workspace, plugin);
                if (configuration is null) { ConnectionError("Save an access token before testing the connection."); return; }
                var generation = sessionGeneration;
                test.IsEnabled = false;
                status.Text = "Checking…";
                try {
                    var result = await active.Request(Guid.NewGuid().ToString(), "plugin:test", configuration);
                    if (generation != sessionGeneration) return;
                    var message = result?["message"]?.GetValue<string>() ?? "The check was not completed. Try again.";
                    if (result?["success"]?.GetValue<bool>() == true) status.Text = message;
                    else ConnectionError(message);
                } catch {
                    if (generation == sessionGeneration) ConnectionError("The check was not completed. Try again.");
                } finally { if (generation == sessionGeneration) test.IsEnabled = true; }
            });
            AutomationProperties.SetAutomationId(test, "PluginTest-" + plugin.Id);
            actions.Children.Add(test);

            enabled.Checked += (_, _) => {
                if (PluginCatalog.Configuration(workspace, plugin) is null) {
                    enabled.IsChecked = false;
                    ConnectionError("Save an access token before enabling this plugin.");
                    return;
                }
                workspace.SetSetting(plugin.Setting, "true");
            };
            enabled.Unchecked += (_, _) => workspace.SetSetting(plugin.Setting, "false");
            content.Children.Add(actions);
            content.Children.Add(enabled);
            content.Children.Add(status);
        }
    }
}
