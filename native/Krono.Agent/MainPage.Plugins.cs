using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Krono_Agent;

public sealed partial class MainPage
{
    sealed record PluginOperation(string Plugin, string Request, AgentWorker Worker);
    PluginOperation? pluginOperation;
    string? changingPlugin;
    string pluginProgress = "";
    Action? refreshPluginCards;

    void AddPluginSettings(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Install copies the included package to this device. Connected tools are available in Agent and Goal modes.", TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        var filter = new KronoComboBox { Header = "Show plugins", ItemsSource = new[] { "All", "Installed", "Not installed" },
            SelectedIndex = 0 };
        AutomationProperties.SetAutomationId(filter, "PluginFilter");
        panel.Children.Add(filter);
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        panel.MaxWidth = double.PositiveInfinity;
        var cards = new List<(PluginCatalog.Plugin Plugin, Border Card)>();
        int columnCount = 0;
        void Arrange(double width, bool refresh = false)
        {
            var columns = Math.Clamp((int)((width + grid.ColumnSpacing) / (245 + grid.ColumnSpacing)), 1, 4);
            if (columns == columnCount && !refresh) return;
            columnCount = columns;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            int position = 0;
            foreach (var (plugin, card) in cards) {
                var visible = filter.SelectedIndex <= 0 || PluginCatalog.Installed(workspace, plugin) == (filter.SelectedIndex == 1);
                card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (!visible) continue;
                if (position % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(card, position / columns);
                Grid.SetColumn(card, position % columns);
                position++;
            }
        }
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        AutomationProperties.SetAutomationId(grid, "PluginCatalog");
        panel.Children.Add(grid);
        var refresh = new List<Action>();
        for (var i = 0; i < PluginCatalog.All.Length; i++) {
            var plugin = PluginCatalog.All[i];

            var card = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1, 1, 1, 3) };
            AutomationProperties.SetAutomationId(card, "Plugin-" + plugin.Id);
            grid.Children.Add(card);
            cards.Add((plugin, card));
            var contents = new Grid { RowSpacing = 10 };
            contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contents.RowDefinitions.Add(new RowDefinition());
            contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Child = contents;

            var header = new Grid { ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(PluginIcon(plugin));
            var title = new TextBlock { Text = plugin.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            contents.Children.Add(header);

            var description = new TextBlock { Text = plugin.Description, TextWrapping = TextWrapping.Wrap, Opacity = .8 };
            Grid.SetRow(description, 1);
            contents.Children.Add(description);
            var footer = new StackPanel { Spacing = 6 };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .76 };
            AutomationProperties.SetAutomationId(status, "PluginProgress-" + plugin.Id);
            footer.Children.Add(actions);
            var badge = new TextBlock { Text = "Installed", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = .8 };
            AutomationProperties.SetAutomationId(badge, "PluginStatus-" + plugin.Id);
            Grid.SetRow(footer, 2);
            contents.Children.Add(footer);

            var primary = Button("Install", async () => {
                if (pluginOperation is { Plugin: var current } && current == plugin.Id) { await CancelPluginConnection(); return; }
                if (pluginOperation is not null || changingPlugin is not null) return;
                if (!PluginCatalog.Installed(workspace, plugin)) { await ChangePlugin(plugin, true); return; }
                if (plugin.RequiresAccount) await ConnectPlugin(plugin);
            });
            AutomationProperties.SetAutomationId(primary, "PluginPrimary-" + plugin.Id);
            footer.Children.Insert(0, primary);

            var options = KronoDesign.CreateMenu();
            var reconnect = new MenuFlyoutItem { Text = "Reconnect" };
            reconnect.Click += async (_, _) => await ConnectPlugin(plugin);
            var check = new MenuFlyoutItem { Text = plugin.RequiresAccount ? "Check connection" : "Check runtime" };
            AutomationProperties.SetAutomationId(check, "PluginTest-" + plugin.Id);
            check.Click += async (_, _) => await CheckPlugin(plugin, status);
            if (plugin.RequiresAccount) options.Items.Add(reconnect);
            if (plugin.RequiresAccount || plugin.Id == "node") options.Items.Add(check);
            if (plugin.SetupUrl is { } url) {
                var guide = new MenuFlyoutItem { Text = "Setup guide" };
                guide.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                options.Items.Add(guide);
            }
            var remove = Button("", async () => {
                var generation = sessionGeneration;
                if (changingPlugin is not null || pluginOperation is not null) return;
                if (!await Confirm("Remove " + plugin.Name + "?", "Krono will stop using this plugin and clear its saved connection on this computer.", "Remove plugin") || generation != sessionGeneration) return;
                await ChangePlugin(plugin, false);
            });
            remove.Content = new SymbolIcon(Symbol.Delete);
            remove.Foreground = KronoDesign.ThemeBrush("KronoDangerForeground", card.ActualTheme);
            card.ActualThemeChanged += (_, _) => remove.Foreground = KronoDesign.ThemeBrush("KronoDangerForeground", card.ActualTheme);
            AutomationProperties.SetName(remove, "Remove " + plugin.Name);
            ToolTipService.SetToolTip(remove, "Remove " + plugin.Name);
            AutomationProperties.SetAutomationId(remove, "PluginRemove-" + plugin.Id);
            actions.Children.Add(remove);
            var more = new Button { Content = "…", MinWidth = 38 };
            more.Click += (_, _) => options.ShowAt(more);
            AutomationProperties.SetName(more, plugin.Name + " options");
            AutomationProperties.SetAutomationId(more, "PluginOptions-" + plugin.Id);
            actions.Children.Add(more);
            actions.Children.Add(badge);
            footer.Children.Add(status);

            void Render()
            {
                var installed = PluginCatalog.Installed(workspace, plugin);
                var connected = installed && plugin.RequiresAccount && PluginCatalog.Configuration(workspace, plugin) is not null;
                var connecting = pluginOperation?.Plugin == plugin.Id;
                bool actionNeeded = !installed || plugin.RequiresAccount && !connected || connecting;
                primary.Content = connecting ? "Cancel" : installed ? "Connect" : "Install";
                primary.Visibility = actionNeeded ? Visibility.Visible : Visibility.Collapsed;
                primary.IsEnabled = connecting || changingPlugin is null && pluginOperation is null;
                remove.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
                remove.IsEnabled = changingPlugin is null && pluginOperation is null;
                more.Visibility = installed && (connected || plugin.Id == "node" || plugin.SetupUrl is not null) ? Visibility.Visible : Visibility.Collapsed;
                more.IsEnabled = changingPlugin is null && pluginOperation is null;
                reconnect.Visibility = connected && !connecting ? Visibility.Visible : Visibility.Collapsed;
                check.Visibility = installed && (connected || plugin.Id == "node") && !connecting ? Visibility.Visible : Visibility.Collapsed;
                badge.Visibility = installed && !connecting ? Visibility.Visible : Visibility.Collapsed;
                badge.Text = connected ? "Connected" : "Installed";
                status.Text = connecting ? pluginProgress : "";
                status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }

            void Paint()
            {
                card.Background = KronoDesign.ThemeBrush("KronoSurface", card.ActualTheme);
                card.BorderBrush = KronoDesign.ThemeBrush("KronoBorder", card.ActualTheme);
            }
            Paint();
            card.ActualThemeChanged += (_, _) => Paint();
            refresh.Add(Render);
            Render();
        }
        refreshPluginCards = () => {
            foreach (var render in refresh) render();
            Arrange(grid.ActualWidth, refresh: true);
        };
        filter.SelectionChanged += (_, _) => Arrange(grid.ActualWidth, refresh: true);
        Arrange(panel.ActualWidth);
        grid.Loaded += (_, _) => { Arrange(panel.ActualWidth); refreshPluginCards?.Invoke(); };
    }

    async Task ChangePlugin(PluginCatalog.Plugin plugin, bool install)
    {
        if (changingPlugin is not null || pluginOperation is not null) return;
        changingPlugin = plugin.Id;
        var generation = sessionGeneration;
        refreshPluginCards?.Invoke();
        try {
            var ready = new TaskCompletionSource();
            if (DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ready.SetResult()))
                await ready.Task;
            if (generation != sessionGeneration) return;
            if (install) PluginCatalog.Install(workspace, plugin);
            else PluginCatalog.Remove(workspace, plugin);
            ShowNotice(plugin.Name + (install ? " installed." : " removed."));
        } finally {
            changingPlugin = null;
            refreshPluginCards?.Invoke();
        }
    }

    static FrameworkElement PluginIcon(PluginCatalog.Plugin plugin)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Plugins", plugin.Id + ".svg");
        var tint = plugin.Color.TrimStart('#');
        var color = Convert.ToInt32(tint, 16);
        var face = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        var tile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = face };
        tile.Child = File.Exists(path) ? new Image { Width = 19, Height = 19, Source = new SvgImageSource(new Uri($"ms-appx:///Assets/Plugins/{plugin.Id}.svg")) }
            : new TextBlock { Text = plugin.Icon, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 24, 32)),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return tile;
    }

    async Task ConnectPlugin(PluginCatalog.Plugin plugin)
    {
        if (pluginOperation is not null || worker is not { } active || !PluginCatalog.Installed(workspace, plugin)) return;
        var generation = sessionGeneration;
        var request = Guid.NewGuid().ToString();
        var operation = new PluginOperation(plugin.Id, request, active);
        var profile = plugin.UsesCli ? workspace.NewPluginProfile(plugin.Id) : null;
        bool retained = false;
        pluginOperation = operation;
        pluginProgress = "Opening provider sign-in…";
        refreshPluginCards?.Invoke();
        async void ShowAuthorize(string id, string kind, JsonNode? value)
        {
            if (id != request || generation != sessionGeneration || pluginOperation != operation) return;
            if (kind == "plugin:status") {
                pluginProgress = value?.ToString() ?? "Complete provider sign-in in your browser.";
                refreshPluginCards?.Invoke();
            } else if (kind == "plugin:authorize" && Uri.TryCreate(value?.ToString(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0) {
                try {
                    if (!await Windows.System.Launcher.LaunchUriAsync(uri)) throw new InvalidOperationException();
                    if (generation != sessionGeneration || pluginOperation != operation) return;
                    if (!pluginProgress.StartsWith("Enter ", StringComparison.Ordinal)) pluginProgress = "Approve the connection in your browser.";
                    refreshPluginCards?.Invoke();
                } catch { if (pluginOperation == operation) await CancelPluginConnection(); }
            }
        }
        active.Event += ShowAuthorize;
        try {
            var reply = await active.Request(request, "plugin:connect", new { id = plugin.Id, profile });
            if (generation != sessionGeneration || !PluginCatalog.Installed(workspace, plugin)) return;
            if (reply?["success"]?.GetValue<bool>() == true) {
                PluginCatalog.SaveConnection(workspace, plugin, reply, profile);
                retained = true;
                ShowNotice(plugin.Name + " connected.");
            } else Error(new InvalidOperationException(reply?["message"]?.ToString() ?? "Could not connect this plugin."));
        } catch (Exception error) { if (generation == sessionGeneration) Error(error); }
        finally {
            active.Event -= ShowAuthorize;
            if (!retained && generation == sessionGeneration && profile is not null) workspace.ClearPluginProfile(plugin.Id, profile);
            if (pluginOperation == operation) { pluginOperation = null; pluginProgress = ""; }
            if (generation == sessionGeneration) refreshPluginCards?.Invoke();
        }
    }

    async Task CancelPluginConnection()
    {
        if (pluginOperation is not { } operation) return;
        pluginProgress = "Cancelling sign-in…";
        refreshPluginCards?.Invoke();
        try { await operation.Worker.Send(Guid.NewGuid().ToString(), "cancel", new { id = operation.Request }); }
        catch (IOException) { }
    }

    async Task CheckPlugin(PluginCatalog.Plugin plugin, TextBlock status)
    {
        if (worker is not { } active || PluginCatalog.Configuration(workspace, plugin) is not { } configuration) return;
        var generation = sessionGeneration;
        status.Text = "Checking…";
        status.Visibility = Visibility.Visible;
        try {
            var reply = await active.Request(Guid.NewGuid().ToString(), "plugin:test", configuration);
            if (generation != sessionGeneration || !PluginCatalog.Installed(workspace, plugin)) return;
            status.Text = "";
            status.Visibility = Visibility.Collapsed;
            var success = reply?["success"]?.GetValue<bool>() == true;
            ShowNotice(plugin.Name + (success ? " check succeeded." : " check failed."), success);
        } catch (Exception error) { if (generation == sessionGeneration) Error(error); }
    }
}
