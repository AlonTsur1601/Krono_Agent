using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Automation;

namespace Krono_Agent;

public sealed partial class MainPage : Page
{
    readonly Workspace workspace = new();
    AgentWorker? worker;
    string? chatId, runId, approvalId;
    JsonNode? approvalDetails;
    ContentDialog? approvalDialog;
    string? activityChatId;
    string provider = "openai", model = "", mode = "chat", permissions = "ask";
    readonly ListView chatList = new() { Width = 220, SelectionMode = ListViewSelectionMode.Single };
    readonly StackPanel messages = new() { Spacing = 20, MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox composer = new() { PlaceholderText = "Message Krono…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 180 };
    readonly ScrollViewer transcript;
    readonly TextBlock progress = new() { Text = "", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    readonly Button modelChoice = new() { Content = "Choose model" };
    readonly Button send = new() { Content = "Send" };
    readonly Button stop = new() { Content = "Stop", IsEnabled = false };
    Grid? chatView;
    ComboBox? permissionSelector, modeSelector;
    int sessionGeneration;
    string? loadedContext;
    TextBlock? streaming;
    string fullResponse = "";
    BrowserWorkspace? browser;
    TerminalWorkspace? terminal;
    GamesWorkspace? games;
    readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    bool refreshingSession;
    public MainPage()
    {
        InitializeComponent(); transcript = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(composer, "Composer");
        modelChoice.Click += (_, _) => Nav.SelectedItem = Nav.FooterMenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "settings");
        sessionTimer.Tick += async (_, _) => {
            if (refreshingSession || workspace.AccountId.Length == 0) return;
            refreshingSession = true;
            try { if (!await workspace.Restore()) await Leave(false); }
            catch (Exception error) { Error(error); }
            finally { refreshingSession = false; }
        };
        Loaded += async (_, _) => {
            try { if (await workspace.Restore()) Enter(); }
            catch (Exception error) { AuthError.Text = error.Message; }
            finally { StartupProgress.IsActive = false; StartupProgress.Visibility = Visibility.Collapsed; if (Nav.Visibility != Visibility.Visible) SignIn.Visibility = Visibility.Visible; }
        };
        Unloaded += (_, _) => { sessionGeneration++; sessionTimer.Stop(); sideChat?.Dispose(); worker?.Dispose(); browser?.Dispose(); terminal?.Dispose(); games?.Dispose(); workspace.Dispose(); };
    }
    async void Login(object sender, RoutedEventArgs e) => await Authenticate(false);
    async void Register(object sender, RoutedEventArgs e) => await Authenticate(true);
    async Task Authenticate(bool register)
    {
        LoginButton.IsEnabled = RegisterButton.IsEnabled = false; AuthProgress.IsActive = true; AuthError.Text = "";
        try { await workspace.Login(EmailInput.Text.Trim(), PasswordInput.Password, register); PasswordInput.Password = ""; Enter(); }
        catch (Exception error) { AuthError.Text = error.Message; }
        finally { LoginButton.IsEnabled = RegisterButton.IsEnabled = true; AuthProgress.IsActive = false; }
    }
    void Enter()
    {
        worker = new AgentWorker(); worker.Event += WorkerEvent;
        SignIn.Visibility = Visibility.Collapsed; Nav.Visibility = Visibility.Visible;
        provider = workspace.Setting("native.provider"); if (provider.Length == 0) provider = "openai";
        model = workspace.Setting("native.model." + provider); if (model.Length == 0 && !CompatibleEndpoint) { model = workspace.Setting("native.model"); workspace.SetSetting("native.model." + provider, model); workspace.SetSetting("native.model", ""); } KronoDesign.ApplyTypography(workspace, this); ApplyTheme(workspace.Setting("native.theme"));
        foreach (var id in ProviderCatalog.Ids.Where(ProviderCatalog.Compatible)) {
            if (workspace.Setting("native.tools." + id) != "true") continue;
            var previousModel = workspace.Setting("native.model." + id); var key = "native.tools." + id + "." + previousModel;
            if (previousModel.Length > 0 && workspace.Setting(key).Length == 0) workspace.SetSetting(key, "true");
            workspace.SetSetting("native.tools." + id, "");
        }
        workspace.FlushSideTransfers(); ShowChats();
        if (workspace.IsOfflineSession) progress.Text = "Offline · local workspace restored; account validation will retry.";
        sessionTimer.Start();
    }
    async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (workspace.AccountId.Length == 0 || args.SelectedItem is not NavigationViewItem item) return;
        try {
            switch (item.Tag?.ToString()) {
                case "chats": ShowChats(); break;
                case "projects": ShowProjects(); break;
                case "settings": ShowSettings(); break;
                case "browser": browser ??= new BrowserWorkspace(workspace, this); KronoDesign.Present(Body, browser); break;
                case "terminal": terminal ??= new TerminalWorkspace(worker!, DispatcherQueue); KronoDesign.Present(Body, terminal); break;
                case "games": games ??= new GamesWorkspace(workspace); KronoDesign.Present(Body, games); break;
                case "logout":
                    await Leave(false); break;
            }
            UpdateActivityVisibility();
        } catch (Exception error) { Error(error); }
    }
    public void Error(Exception error) { Notice.Message = error.Message; Notice.IsOpen = true; }
    public void PauseGames() => games?.PauseAndSave();
    async Task Leave(bool allDevices)
    {
        sessionGeneration++;
        ClearApprovalDetails();
        CloseSideChat();
        sessionTimer.Stop(); worker?.Dispose(); worker = null; browser?.Dispose(); browser = null; terminal?.Dispose(); terminal = null; games?.Dispose(); games = null;
        try { await workspace.Logout(allDevices); }
        finally {
            chatId = runId = loadedContext = approvalId = null; Body.Content = null; chatList.ItemsSource = null; chatList.IsEnabled = send.IsEnabled = true; stop.IsEnabled = false;
            messages.Children.Clear(); composer.Text = fullResponse = progress.Text = ""; streaming = null; model = ""; modelChoice.Content = "Choose model";
            mode = "chat"; permissions = "ask"; if (modeSelector is not null) modeSelector.SelectedItem = mode; if (permissionSelector is not null) permissionSelector.SelectedItem = permissions;
            activityChatId = null; RunActivity.IsOpen = false; Approval.IsOpen = Notice.IsOpen = false; Nav.Visibility = Visibility.Collapsed; SignIn.Visibility = Visibility.Visible;
        }
    }
    public async Task<bool> Confirm(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = "Allow", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    static TextBlock Heading(string text) => new() { Text = text, FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
    Button Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => { try { action(); } catch (Exception error) { Error(error); } }; return button; }
    Button Button(string label, Func<Task> action) { var button = new Button { Content = label }; button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Error(error); } }; return button; }
    static ComboBox Choices(string label, string[] choices, string selected, Action<string> change)
    {
        var box = new ComboBox { Header = label, MinWidth = 120, ItemsSource = choices, SelectedItem = selected };
        AutomationProperties.SetAutomationId(box, label.Replace(" ", ""));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string value) change(value); }; return box;
    }
    void ShowChats()
    {
        if (chatView is null) {
            chatView = new Grid { ColumnSpacing = 24 }; chatView.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); chatView.ColumnDefinitions.Add(new ColumnDefinition());
            var history = new StackPanel { Spacing = 12, Width = 220 }; history.Children.Add(Heading("Chats"));
            history.Children.Add(Button("+ New chat", () => { if (runId is null) { chatId = workspace.CreateChat(); ReloadChats(); LoadChat(); } }));
            chatList.DisplayMemberPath = "Name"; chatList.SelectionChanged += (_, _) => { if (runId is null && chatList.SelectedItem is ChatItem item) { chatId = item.Id; LoadChat(); } }; history.Children.Add(chatList);
            history.Children.Add(Button("Delete selected chat", () => { if (chatId is not null && runId is null) { workspace.Execute("UPDATE chats SET deleted_at=$p0 WHERE id=$p1", Workspace.Now, chatId); chatId = null; ReloadChats(); LoadChat(); } }));
            chatView.Children.Add(history);
            var conversation = new Grid { RowSpacing = 12 }; Grid.SetColumn(conversation, 1); chatView.Children.Add(conversation);
            foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) conversation.RowDefinitions.Add(new RowDefinition { Height = height });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            modeSelector = Choices("Mode", ["chat", "plan", "agent", "goal"], mode, value => mode = value); modeSelector.MinWidth = 110; toolbar.Children.Add(modeSelector); permissionSelector = Choices("Permissions", ["ask", "project", "full"], permissions, value => permissions = value); permissionSelector.MinWidth = 110; toolbar.Children.Add(permissionSelector);
            ToolTipService.SetToolTip(permissionSelector, "Ask: approve changes/commands. Project: bounded file tools; no shell or desktop execution. Full: supplied tools run with your Windows account.");
            toolbar.Children.Add(Button("Project…", async () => {
                if (chatId is null || runId is not null) return;
                var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name");
                var selector = new ComboBox { MinWidth = 260, ItemsSource = new[] { "No project" }.Concat(projects.Select(p => p["name"]!.ToString())).ToArray(), SelectedIndex = 0 };
                AutomationProperties.SetAutomationId(selector, "ChatProjectSelector");
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Move chat to project", Content = selector, PrimaryButtonText = "Move", CloseButtonText = "Cancel" };
                dialog.PrimaryButtonClick += (_, _) => { workspace.Execute("UPDATE chats SET project_id=$p0,updated_at=$p1 WHERE id=$p2", selector.SelectedIndex > 0 ? projects[selector.SelectedIndex - 1]["id"]!.ToString() : null, Workspace.Now, chatId); LoadChat(); };
                await dialog.ShowAsync();
            }));
            conversation.Children.Add(toolbar); Grid.SetRow(transcript, 1); conversation.Children.Add(transcript);
            Grid.SetRow(progress, 2); conversation.Children.Add(progress);
            var input = new StackPanel { Spacing = 10 }; input.Children.Add(composer); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            send.Style = (Style)Application.Current.Resources["AccentButtonStyle"]; send.Click += async (_, _) => await SendMessage();
            stop.Click += async (_, _) => { if (runId is not null) await worker!.Send(Guid.NewGuid().ToString(), "cancel", new { id = runId }); };
            var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; primaryActions.Children.Add(send); primaryActions.Children.Add(stop);
            var secondaryActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; modelChoice.MaxWidth = 210; secondaryActions.Children.Add(modelChoice); secondaryActions.Children.Add(Button("Side chat", OpenSideChat));
            actions.Children.Add(primaryActions); actions.Children.Add(secondaryActions); actions.SizeChanged += (_, args) => actions.Orientation = args.NewSize.Width < 500 ? Orientation.Vertical : Orientation.Horizontal;
            input.Children.Add(actions); Grid.SetRow(input, 3); conversation.Children.Add(input);
        }
        modelChoice.Content = new TextBlock { Text = model.Length == 0 ? "Choose model" : model, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(modelChoice, ProviderCatalog.Name(provider) + " / " + model);
        KronoDesign.Present(Body, chatView); ReloadChats(); if (runId is null) LoadChat();
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "chats");
        UpdateActivityVisibility();
    }
    record ChatItem(string Id, string Name);
    void ReloadChats() { chatList.ItemsSource = workspace.Query("SELECT id,name FROM chats WHERE deleted_at IS NULL AND id NOT IN (SELECT chat_id FROM side_chats) ORDER BY updated_at DESC").Select(row => new ChatItem(row["id"]!.ToString(), row["name"]!.ToString())).ToList(); }
    void LoadChat()
    {
        messages.Children.Clear();
        if (chatId is null) {
            var welcome = new StackPanel { Spacing = 18, Margin = new Thickness(0, 80, 0, 0), MaxWidth = 520 };
            welcome.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Left });
            welcome.Children.Add(new TextBlock { Text = "What would you like to work on?", FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            welcome.Children.Add(new TextBlock { Text = "Start a conversation, work on a project, or pick up where you left off.", FontSize = 15, Opacity = .65, TextWrapping = TextWrapping.Wrap });
            welcome.Children.Add(Button("+ Start a conversation", () => { chatId = workspace.CreateChat(); ReloadChats(); LoadChat(); }));
            messages.Children.Add(welcome); return;
        }
        var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
        if (loadedContext != chatId + ":" + project) {
            loadedContext = chatId + ":" + project;
            var config = project is null ? null : JsonNode.Parse(workspace.Setting("project." + project));
            permissions = config?["permissions"]?.ToString() ?? "ask";
            if (permissionSelector is not null) permissionSelector.SelectedItem = permissions;
        }
        foreach (var message in workspace.Query("SELECT id,role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at,rowid", chatId)) AddBubble(message["role"]!.ToString(), message["content"]?.ToString() ?? "", message["id"]!.ToString());
    }
    void AddBubble(string role, string text, string messageId)
    {
        var box = new StackPanel { Spacing = 8 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new TextBlock { Text = role == "user" ? "You" : "Krono", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = .6, VerticalAlignment = VerticalAlignment.Center });
        var branch = Button("Branch", () => OpenSideChatFrom(messageId)); branch.Padding = new Thickness(8, 3, 8, 3); branch.FontSize = 11; branch.Tag = "caption";
        AutomationProperties.SetName(branch, "Side chat from this message"); header.Children.Add(branch); box.Children.Add(header);
        box.Children.Add(Markdown(text));
        if (role == "user") messages.Children.Add(new Border { Child = box, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 143, 215, 199)), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 143, 215, 199)), BorderThickness = new Thickness(1) });
        else messages.Children.Add(box);
    }
    RichTextBlock Markdown(string text) => NativeMarkdown.Render(text,
        KronoDesign.TextFont.Source, KronoDesign.TextSize, KronoDesign.CodeFont.Source, KronoDesign.CodeSize,
        workspace.Setting("native.codePalette"));
    async Task SendMessage()
    {
        if (runId is not null || string.IsNullOrWhiteSpace(composer.Text)) return;
        var generation = sessionGeneration;
        try {
            var apiKey = ConnectionKey();
            chatId ??= workspace.CreateChat(); var text = composer.Text; composer.Text = "";
            workspace.AddMessage(chatId, "user", text); if (workspace.Query("SELECT id FROM messages WHERE chat_id=$p0", chatId).Count == 1) workspace.Execute("UPDATE chats SET name=$p0 WHERE id=$p1", text[..Math.Min(42, text.Length)], chatId);
            LoadChat(); ReloadChats(); streaming = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["KronoTextFont"], FontSize = (double)Application.Current.Resources["KronoTextSize"], LineHeight = 24 }; messages.Children.Add(streaming); fullResponse = "";
            var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
            var config = project is null ? null : JsonNode.Parse(workspace.Setting("project." + project));
            var dirs = project is null ? [] : workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", project).Select(p => p["path"]!.ToString()).ToArray();
            var memories = workspace.Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", project).Select(p => p["content"]!.ToString()).ToArray();
            runId = Guid.NewGuid().ToString(); send.IsEnabled = false; chatList.IsEnabled = false; stop.IsEnabled = true; progress.Text = "Working…";
            activityChatId = chatId; RunActivity.Title = "Agent running"; RunActivity.Message = workspace.Query("SELECT name FROM chats WHERE id=$p0", chatId).First()["name"]?.ToString(); RunActivity.IsClosable = false; UpdateActivityVisibility();
            var history = workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at", chatId).Select(p => new { role = p["role"]!.ToString(), content = p["content"]!.ToString() }).ToArray();
            await worker!.Request(runId, "run", new { providerId = provider, apiKey, endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled, modelId = model, mode, permissions, messages = history, projectDirs = dirs, instructions = config?["instructions"]?.ToString() ?? "", memories });
            if (generation != sessionGeneration) return;
            workspace.AddMessage(chatId, "assistant", fullResponse); progress.Text = "Done";
            RunActivity.Title = "Response ready"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility();
        } catch (Exception error) { if (generation == sessionGeneration) { Error(error); progress.Text = "Stopped"; RunActivity.Title = "Agent stopped"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility(); } }
        finally { if (generation == sessionGeneration) { runId = null; send.IsEnabled = true; chatList.IsEnabled = true; stop.IsEnabled = false; Approval.IsOpen = false; approvalId = null; ClearApprovalDetails(); if (workspace.AccountId.Length > 0) { workspace.FlushSideTransfers(); LoadChat(); } } }
    }
    void WorkerEvent(string id, string type, JsonNode? data) {
        terminal?.Receive(id, type, data);
        if (id != runId) return;
        if (type == "approval") { approvalId = data!["id"]!.ToString(); approvalDetails = data["details"]?.DeepClone(); var target = data["target"]?.ToString() ?? ""; Approval.Message = data["action"] + " · " + (target.Length > 240 ? target[..240] + "…" : target); Approval.IsOpen = true; RunActivity.Title = "Approval needed"; }
        if (type == "chunk" && data?["type"]?.ToString() == "text") { fullResponse += data["content"]?.ToString(); if (streaming is not null) streaming.Text = fullResponse; transcript.ChangeView(null, transcript.ScrollableHeight, null); }
        if (type == "chunk" && data?["type"]?.ToString() == "tool_call") { progress.Text = "Using " + data["toolCall"]?["name"]; RunActivity.Title = progress.Text; }
    }
    void UpdateActivityVisibility() => RunActivity.IsOpen = activityChatId is not null && !ReferenceEquals(Body.Content, chatView);
    void ActivityClosed(InfoBar sender, InfoBarClosedEventArgs args) { if (runId is null) activityChatId = null; }
    void OpenActivityChat(object sender, RoutedEventArgs args) {
        try { if (activityChatId is null) return; var id = activityChatId; games?.PauseForAgent(); OpenNormalChat(id); if (runId is null) activityChatId = null; UpdateActivityVisibility(); }
        catch (Exception error) { Error(error); }
    }
    async void Approve(object sender, RoutedEventArgs args) { try { await AnswerApproval(true); } catch (Exception error) { Error(error); } }
    async void Deny(object sender, RoutedEventArgs args) { try { await AnswerApproval(false); } catch (Exception error) { Error(error); } }
    async Task AnswerApproval(bool approved) { if (approvalId is null) return; await worker!.Send(Guid.NewGuid().ToString(), "approve", new { id = approvalId, approved }); approvalId = null; ClearApprovalDetails(); Approval.IsOpen = false; }
    void ClearApprovalDetails() { if (approvalDialog?.Content is TextBox content) content.Text = ""; approvalDialog?.Hide(); approvalDialog = null; approvalDetails = null; }
    async void ReviewApproval(object sender, RoutedEventArgs args) {
        if (approvalDetails is null || approvalDialog is not null) return;
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 350, MaxHeight = 480, FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize, Text = approvalDetails.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) };
        AutomationProperties.SetAutomationId(details, "ToolApprovalDetails");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Action details", Content = details, CloseButtonText = "Close details" }; approvalDialog = dialog;
        try { await dialog.ShowAsync(); } catch (Exception error) { Error(error); } finally { if (ReferenceEquals(approvalDialog, dialog)) approvalDialog = null; }
    }
    void ApplyTheme(string value)
    {
        RequestedTheme = value == "light" ? ElementTheme.Light : value is "dark" or "halloween" ? ElementTheme.Dark : ElementTheme.Default;
        if (MainWindow.Instance?.Content is FrameworkElement root) root.RequestedTheme = RequestedTheme;
        KronoDesign.ApplyPalette(value == "halloween");
    }
    bool CompatibleEndpoint => ProviderCatalog.Compatible(provider);
    string ProviderEndpoint => workspace.Setting("native.endpoint." + provider) is { Length: > 0 } endpoint ? endpoint : ProviderCatalog.Endpoint(provider);
    string ModelToolsSetting => "native.tools." + provider + "." + model;
    bool ProviderToolsEnabled => !CompatibleEndpoint || model.Length > 0 && workspace.Setting(ModelToolsSetting) == "true";
    string ConnectionKey() {
        if (model.Length == 0) throw new InvalidOperationException("Choose a provider and model in Settings first.");
        var endpoint = CompatibleEndpoint ? ProviderCatalog.ParseEndpoint(ProviderEndpoint) : null;
        var key = workspace.Key(provider);
        if (key.Length == 0 && (ProviderCatalog.RequiresKey(provider) || endpoint is not null && !ProviderCatalog.Local(endpoint))) throw new InvalidOperationException("Save this provider's API key in Settings first.");
        return key;
    }
    void ShowSettings()
    {
        var panel = new StackPanel { Spacing = 18, MaxWidth = 650, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(Heading("Settings"));
        panel.Children.Add(new TextBlock { Text = workspace.Email, Opacity = .65 });
        panel.Children.Add(Button("Sign out on all devices", async () => { if (await Confirm("Sign out on all devices", "End this account's refresh sessions on all devices?")) await Leave(true); }));
        panel.Children.Add(Choices("Appearance", ["system", "dark", "light", "halloween"], workspace.Setting("native.theme") is { Length: > 0 } t ? t : "system", value => { workspace.SetSetting("native.theme", value); ApplyTheme(value); }));
        var secret = new PasswordBox { Header = "Provider API key · optional for local servers", MinWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        var endpoint = new TextBox { Header = "Server base URL · include /v1", Text = ProviderEndpoint, MinWidth = 420 };
        AutomationProperties.SetAutomationId(endpoint, "ProviderEndpoint");
        var tools = new CheckBox { Content = "This model supports tool calling", IsChecked = ProviderToolsEnabled };
        AutomationProperties.SetAutomationId(tools, "ProviderTools");
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7 }; AutomationProperties.SetAutomationId(connectionStatus, "ProviderConnectionStatus");
        var models = new ComboBox { Header = "Model", MinWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, Text = model };
        var profileHelp = new TextBlock { Text = ProviderCatalog.Help(provider), TextWrapping = TextWrapping.Wrap, Opacity = .65 };
        var saveEndpoint = Button("Save endpoint", () => { try {
            var url = ProviderCatalog.ParseEndpoint(endpoint.Text);
            workspace.SetSetting("native.endpoint." + provider, url.AbsoluteUri.TrimEnd('/')); connectionStatus.Text = "Endpoint saved.";
        } catch (Exception error) { Error(error); } });
        void EndpointVisibility() { endpoint.Visibility = tools.Visibility = saveEndpoint.Visibility = CompatibleEndpoint ? Visibility.Visible : Visibility.Collapsed; tools.IsEnabled = model.Length > 0; }
        ToolTipService.SetToolTip(tools, "Choose Use model first. Tool support is saved separately for each provider and model.");
        var providerIds = ProviderCatalog.Ids; var providerNames = providerIds.Select(ProviderCatalog.Name).ToArray();
        panel.Children.Add(Choices("Provider", providerNames, ProviderCatalog.Name(provider), value => {
            provider = providerIds[Array.IndexOf(providerNames, value)]; workspace.SetSetting("native.provider", provider); secret.Password = ""; model = workspace.Setting("native.model." + provider); models.ItemsSource = null; models.Text = model; endpoint.Text = ProviderEndpoint; tools.IsChecked = ProviderToolsEnabled; connectionStatus.Text = ""; profileHelp.Text = ProviderCatalog.Help(provider); EndpointVisibility();
        }));
        tools.Checked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "true"); }; tools.Unchecked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "false"); };
        EndpointVisibility(); panel.Children.Add(endpoint); panel.Children.Add(saveEndpoint); panel.Children.Add(tools); panel.Children.Add(secret);
        AutomationProperties.SetAutomationId(secret, "ProviderKey");
        panel.Children.Add(Button("Save key", () => { if (!string.IsNullOrWhiteSpace(secret.Password)) { workspace.SaveKey(provider, secret.Password.Trim()); secret.Password = ""; } }));
        panel.Children.Add(Button("Test connection", async () => { try { var result = await worker!.Request(Guid.NewGuid().ToString(), "test", new { providerId = provider, apiKey = workspace.Key(provider), endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled }); connectionStatus.Text = result?["success"]?.GetValue<bool>() == true ? "Connected · " + result["latencyMs"] + " ms" : "Connection failed. Check the server, endpoint and credentials."; } catch (Exception error) { Error(error); } }));
        panel.Children.Add(connectionStatus); panel.Children.Add(models);
        AutomationProperties.SetAutomationId(models, "ModelSelector");
        panel.Children.Add(Button("Load available models", async () => { try { var result = await worker!.Request(Guid.NewGuid().ToString(), "models", new { providerId = provider, apiKey = workspace.Key(provider), endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled }); models.ItemsSource = result!.AsArray().Select(m => m!["id"]!.ToString()).ToArray(); } catch (Exception error) { Error(error); } }));
        panel.Children.Add(Button("Use model", () => { model = models.Text.Trim(); workspace.SetSetting("native.model." + provider, model); tools.IsChecked = ProviderToolsEnabled; tools.IsEnabled = model.Length > 0; }));
        panel.Children.Add(profileHelp);
        var textFont = new TextBox { Header = "Text font", Text = workspace.Setting("native.textFont") is { Length: > 0 } tf ? tf : "Rubik" };
        var codeFont = new TextBox { Header = "Code font", Text = workspace.Setting("native.codeFont") is { Length: > 0 } cf ? cf : "JetBrains Mono" };
        AutomationProperties.SetAutomationId(textFont, "TextFontFamily"); AutomationProperties.SetAutomationId(codeFont, "CodeFontFamily");
        var textSize = new NumberBox { Header = "Text size", Minimum = 11, Maximum = 28, Value = KronoDesign.TextSize };
        var codeSize = new NumberBox { Header = "Code size", Minimum = 10, Maximum = 26, Value = KronoDesign.CodeSize };
        AutomationProperties.SetAutomationId(textSize, "TextFontSize"); AutomationProperties.SetAutomationId(codeSize, "CodeFontSize");
        panel.Children.Add(textFont); panel.Children.Add(textSize); panel.Children.Add(codeFont); panel.Children.Add(codeSize);
        panel.Children.Add(Choices("Code palette", ["classic", "mint", "halloween"], workspace.Setting("native.codePalette") is { Length: > 0 } palette ? palette : "classic", value => workspace.SetSetting("native.codePalette", value)));
        panel.Children.Add(Button("Save typography", () => {
            workspace.SetSetting("native.textFont", textFont.Text.Trim()); workspace.SetSetting("native.codeFont", codeFont.Text.Trim());
            workspace.SetSetting("native.textSize", (double.IsFinite(textSize.Value) ? Math.Clamp(textSize.Value, 11, 28) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            workspace.SetSetting("native.codeSize", (double.IsFinite(codeSize.Value) ? Math.Clamp(codeSize.Value, 10, 26) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            KronoDesign.ApplyTypography(workspace, this);
        }));
        panel.Children.Add(new TextBlock { Text = "Bundled fonts: Rubik (including Hebrew), Space Grotesk and JetBrains Mono. You can also enter an installed font name. Text and code sizes are independent.", TextWrapping = TextWrapping.Wrap, Opacity = .65 });
        panel.Children.Add(new TextBlock { Text = "API keys are encrypted for this Windows account. Provider usage and billing follow your provider account.", TextWrapping = TextWrapping.Wrap });
        KronoDesign.Present(Body, new ScrollViewer { Content = panel });
    }
    void ShowProjects()
    {
        var panel = new StackPanel { Spacing = 14, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(Heading("Projects & memory"));
        var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name"); string? selected = null;
        var picker = new ComboBox { Header = "Project", ItemsSource = projects.Select(p => p["name"]!.ToString()).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = "Name" }; var folders = new TextBox { Header = "Project folders · one absolute path per line", AcceptsReturn = true, MinHeight = 70 };
        AutomationProperties.SetAutomationId(name, "ProjectName"); AutomationProperties.SetAutomationId(folders, "ProjectFolders");
        var instructions = new TextBox { Header = "Project instructions", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        var memory = new TextBox { Header = "Project memory", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        AutomationProperties.SetAutomationId(instructions, "ProjectInstructions"); AutomationProperties.SetAutomationId(memory, "ProjectMemory");
        var global = new TextBox { Header = "Global memory", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Text = workspace.Query("SELECT content FROM memories WHERE id='global'").FirstOrDefault()?["content"]?.ToString() ?? "" };
        AutomationProperties.SetAutomationId(global, "GlobalMemory");
        string projectPermissions = "ask";
        var permissionBox = Choices("Default permissions", ["ask", "project", "full"], "ask", value => projectPermissions = value);
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex < 0) return; selected = projects[picker.SelectedIndex]["id"]!.ToString(); name.Text = projects[picker.SelectedIndex]["name"]!.ToString(); folders.Text = string.Join(Environment.NewLine, workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", selected).Select(p => p["path"]!.ToString())); var config = JsonNode.Parse(workspace.Setting("project." + selected)); instructions.Text = config?["instructions"]?.ToString() ?? ""; permissionBox.SelectedItem = config?["permissions"]?.ToString() ?? "ask"; memory.Text = workspace.Query("SELECT content FROM memories WHERE id=$p0", "project:" + selected).FirstOrDefault()?["content"]?.ToString() ?? ""; };
        panel.Children.Add(picker); panel.Children.Add(Button("New project", () => { selected = null; picker.SelectedIndex = -1; name.Text = folders.Text = instructions.Text = memory.Text = ""; }));
        panel.Children.Add(name); panel.Children.Add(folders); panel.Children.Add(instructions); panel.Children.Add(permissionBox); panel.Children.Add(memory);
        panel.Children.Add(Button("Add folder…", async () => {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker(); folderPicker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance!));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is not null) folders.Text += (folders.Text.Length == 0 ? "" : Environment.NewLine) + folder.Path;
        }));
        panel.Children.Add(Button("Save project", () => { try {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Give the project a name.");
            var directories = folders.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFullPath).Distinct().ToArray(); if (directories.Any(p => !Directory.Exists(p))) throw new InvalidOperationException("Every project folder must exist.");
            var id = selected ?? Guid.NewGuid().ToString(); var now = Workspace.Now;
            workspace.Execute("BEGIN"); try {
            workspace.Execute("INSERT INTO projects(id,account_id,name,created_at,updated_at) VALUES($p0,$p1,$p2,$p3,$p3) ON CONFLICT(id) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at", id, workspace.AccountId, name.Text.Trim(), now);
            workspace.Execute("DELETE FROM project_directories WHERE project_id=$p0", id); foreach (var directory in directories) workspace.Execute("INSERT INTO project_directories(id,project_id,device_id,path) VALUES($p0,$p1,'local',$p2)", Guid.NewGuid().ToString(), id, directory);
            workspace.SetSetting("project." + id, new JsonObject { ["instructions"] = instructions.Text, ["permissions"] = projectPermissions }.ToJsonString()); SaveMemory("project:" + id, id, memory.Text); workspace.Execute("COMMIT"); } catch { workspace.Execute("ROLLBACK"); throw; } ShowProjects();
        } catch (Exception error) { Error(error); } }));
        panel.Children.Add(global); panel.Children.Add(Button("Save global memory", () => SaveMemory("global", null, global.Text)));
        KronoDesign.Present(Body, new ScrollViewer { Content = panel });
    }
    void SaveMemory(string id, string? project, string content) => workspace.Execute("INSERT INTO memories(id,account_id,project_id,content,created_at,updated_at) VALUES($p0,$p1,$p2,$p3,$p4,$p4) ON CONFLICT(id) DO UPDATE SET content=excluded.content,updated_at=excluded.updated_at", id, workspace.AccountId, project, content, Workspace.Now);
}
