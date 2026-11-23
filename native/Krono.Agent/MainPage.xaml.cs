using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Krono_Agent;

public sealed partial class MainPage : Page
{
    readonly Workspace workspace = new();
    CancellationTokenSource? confirmationWait;
    bool authenticating;
    AgentWorker? worker;
    string? chatId, runId, approvalId;
    string? draftProject;
    JsonNode? approvalDetails;
    ContentDialog? approvalDialog;
    string? activityChatId;
    string provider = "openai", model = "", mode = "chat", permissions = "ask";
    readonly ListView chatList = new() { Width = 220, SelectionMode = ListViewSelectionMode.Single };
    readonly StackPanel messages = new() { Spacing = 20, MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
    readonly KronoTextBox composer = new() { PlaceholderText = "Message Krono…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 180, FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left };
    readonly ScrollViewer transcript;
    readonly TextBlock progress = new() { Text = "", TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    readonly ProgressRing progressPulse = new() { Width = 15, Height = 15, IsActive = false, Visibility = Visibility.Collapsed };
    readonly Button modelChoice = new() { Content = "Choose model" };
    Button? sideChatButton;
    readonly Button send = new() { Content = "Send" };
    Grid? chatView;
    ComboBox? permissionSelector, modeSelector;
    int sessionGeneration;
    string? loadedContext;
    TextBlock? streaming;
    BrowserWorkspace? browser;
    TerminalWorkspace? terminal;
    GamesWorkspace? games;
    readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    bool refreshingSession;
    public MainPage()
    {
        InitializeComponent();
        BrowserNavigation.Icon = BrowserWorkspace.BrowserTabIcon();
        UpdateButton.Icon = KronoDesign.NavigationIcon(Symbol.Download);
        UpdateButton.Icon.Foreground = KronoDesign.ThemeBrush("KronoAccentForeground", ActualTheme);
        ActualThemeChanged += (_, _) => UpdateButton.Icon.Foreground = KronoDesign.ThemeBrush("KronoAccentForeground", ActualTheme); transcript = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
        transcript.SizeChanged += (_, args) => messages.Width = Math.Min(messages.MaxWidth, Math.Max(0, args.NewSize.Width - 16));
        WorkspacePattern.Children.Add(new GameBackdrop());
        LoginPattern.Children.Add(new GameBackdrop());
        Nav.Resources["NavigationViewSelectionIndicatorForeground"] = KronoDesign.ThemeBrush("KronoAccentFill", ElementTheme.Dark);
        void NamePaneToggle() => KronoDesign.NameTemplateButton(Nav, "TogglePaneButton", Nav.IsPaneOpen ? "Close navigation" : "Open navigation");
        Nav.Loaded += (_, _) => NamePaneToggle();
        Nav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => DispatcherQueue.TryEnqueue(NamePaneToggle));
        ToolTipService.SetToolTip(SignOutButton, "Sign out");
        InitializeUpdates();
        InitializeNavigationIconMotion();
        InitializeSync(); InitializeSchedule();
        InitializeChatWorkspace();
        AutomationProperties.SetAutomationId(composer, "Composer");
        composer.Paste += PasteComposerImage;
        composer.PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.V ||
                !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
            if (TryPasteComposerImage()) args.Handled = true;
        };
        AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) => {
            if (reasoningPanel.Visibility != Visibility.Visible) return;
            for (var source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
                if (ReferenceEquals(source, reasoningPanel) || ReferenceEquals(source, modelChoice)) return;
            reasoningPanel.Visibility = Visibility.Collapsed;
        }), true);
        PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Escape || reasoningPanel.Visibility != Visibility.Visible) return;
            reasoningPanel.Visibility = Visibility.Collapsed;
            modelChoice.Focus(FocusState.Keyboard);
            args.Handled = true;
        };
        modelChoice.Click += async (_, _) => { try { await ChooseChatModel(); } catch (Exception error) { Error(error); } };
        sessionTimer.Tick += async (_, _) => {
            if (refreshingSession || workspace.AccountId.Length == 0) return;
            refreshingSession = true; var generation = sessionGeneration;
            try { var restored = await workspace.Restore(); if (generation == sessionGeneration && !restored) await Leave(false); }
            catch (Exception error) { Error(error); }
            finally { refreshingSession = false; }
        };
        Loaded += async (_, _) => {
            try { if (await workspace.Restore()) Enter(); }
            catch (Exception error) { Error(error); }
            finally { StartupProgress.IsActive = false; StartupProgress.Visibility = Visibility.Collapsed; if (Nav.Visibility != Visibility.Visible) SignIn.Visibility = Visibility.Visible; }
        };
        Unloaded += (_, _) => { confirmationWait?.Cancel(); sessionGeneration++; sessionTimer.Stop(); StopSync(); StopSchedules(); sideChat?.Dispose(); worker?.Dispose(); browser?.Dispose(); terminal?.Dispose(); games?.Dispose(); workspace.Dispose(); };
    }
    async void Login(object sender, RoutedEventArgs e) => await Authenticate(false);
    async void SignInWithGoogle(object sender, RoutedEventArgs e)
    {
        if (authenticating) return;
        authenticating = true;
        HideError();
        try {
            if (!await AuthorizeGoogle(false)) return;
            PasswordInput.Password = "";
            PasswordVisibility.IsChecked = false;
            Enter();
        }
        catch (Exception error) { Error(error); }
        finally { authenticating = false; }
    }

    internal async Task<bool> AuthorizeGoogle(bool link)
    {
        using var cancellation = new CancellationTokenSource();
        var dialog = new KronoDialog {
            XamlRoot = XamlRoot,
            Title = link ? "Link your Google account" : "Sign in with Google",
            Content = new TextBlock {
                Text = "Complete Google authorization in your default browser, then return to Krono.",
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Cancel"
        };
        var dismissal = dialog.ShowAsync().AsTask();
        var authorization = workspace.GoogleAuthenticate(link, cancellation.Token);
        if (await Task.WhenAny(dismissal, authorization) == dismissal) cancellation.Cancel();
        else if (!dismissal.IsCompleted) dialog.Hide();
        await dismissal;
        try { await authorization; return true; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
    }
    void Register(object sender, RoutedEventArgs e) => SetRegistrationMode(true);
    async void CreateAccount(object sender, RoutedEventArgs e) => await Authenticate(true);
    async void SubmitPassword(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || e.KeyStatus.WasKeyDown || PasswordInput.FocusState == FocusState.Unfocused) return;
        e.Handled = true;
        await Authenticate(CreateAccountButton.Visibility == Visibility.Visible);
    }
    void SetRegistrationMode(bool register)
    {
        AuthTitle.Text = register ? "Create your account" : "Welcome to Krono";
        AuthSubtitle.Text = register ? "Sign up to start your Krono workspace." : "Sign in to your workspace.";
        LoginButton.Visibility = NewAccountPrompt.Visibility = register ? Visibility.Collapsed : Visibility.Visible;
        CreateAccountButton.Visibility = ExistingAccountPrompt.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
        HideError();
        EmailInput.Focus(FocusState.Programmatic);
    }
    void CenterPasswordContent(object sender, RoutedEventArgs args)
    {
        if (sender is not PasswordBox password) return;
        KronoInputMotion.Attach(password, "KronoPasswordInput", PasswordVisibility);
        KronoInputMotion.StyleButton(PasswordVisibility);
    }
    void PasswordVisibilityChanged(object sender, RoutedEventArgs args)
    {
        var visible = PasswordVisibility.IsChecked == true;
        PasswordInput.PasswordRevealMode = visible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        PasswordEye.Glyph = visible ? "\uED1A" : "\uE890";
        var label = visible ? "Hide password" : "Show password";
        AutomationProperties.SetName(PasswordVisibility, label);
        ToolTipService.SetToolTip(PasswordVisibility, label);
    }
    async Task Authenticate(bool register)
    {
        var submit = register ? CreateAccountButton : LoginButton;
        if (authenticating) return;
        authenticating = true;
        var label = submit.Content;
        submit.Content = new ProgressRing { Width = 18, Height = 18, IsActive = true, Foreground = submit.Foreground };
        submit.IsHitTestVisible = false;
        RegisterButton.IsEnabled = BackToSignInButton.IsEnabled = false;
        HideError();
        var email = EmailInput.Text.Trim();
        var password = PasswordInput.Password;
        try {
            if (!await workspace.Login(email, password, register)) { ShowConfirmation(email, true); _ = WaitForConfirmation(email, password); return; }
            PasswordInput.Password = ""; PasswordVisibility.IsChecked = false; Enter();
        }
        catch (Workspace.EmailConfirmationRequiredException) { ShowConfirmation(email, false); }
        catch (Exception error) { Error(error); }
        finally {
            submit.Content = label;
            submit.IsHitTestVisible = true;
            RegisterButton.IsEnabled = BackToSignInButton.IsEnabled = true;
            authenticating = false;
        }
    }
    async Task WaitForConfirmation(string email, string password)
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        confirmationWait = wait;
        try {
            while (true) {
                await Task.Delay(TimeSpan.FromSeconds(15), wait.Token);
                try {
                    await workspace.Login(email, password, cancellation: wait.Token);
                    Enter();
                    return;
                }
                catch (Workspace.EmailConfirmationRequiredException) { }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!wait.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) {
            ConfirmationStatus.Text = "After confirming your email, return to sign in.";
            ConfirmationStatus.Visibility = Visibility.Visible;
        }
        finally { if (confirmationWait == wait) confirmationWait = null; }
    }
    void ShowConfirmation(string email, bool sent)
    {
        PasswordInput.Password = "";
        PasswordVisibility.IsChecked = false;
        ConfirmationAddress.Text = email;
        ConfirmationInstructions.Text = sent ? "We sent a confirmation link to:" : "Confirm the email address for this account:";
        ConfirmationHelp.Text = (sent
            ? "Open the confirmation link and keep Krono Agent open to sign in automatically."
            : "Open the confirmation link, then return to Krono Agent to sign in.")
            + " If you do not see the email, check your spam or junk folder.";
        ConfirmationStatus.Text = "";
        ConfirmationStatus.Visibility = Visibility.Collapsed;
        AuthCard.Padding = new Thickness(24);
        LoginForm.Visibility = Visibility.Collapsed;
        ConfirmationForm.Visibility = Visibility.Visible;
        BackToLoginButton.Focus(FocusState.Programmatic);
    }
    void BackToLogin(object sender, RoutedEventArgs args)
    {
        confirmationWait?.Cancel();
        ConfirmationForm.Visibility = Visibility.Collapsed;
        LoginForm.Visibility = Visibility.Visible;
        AuthCard.Padding = new Thickness(32);
        SetRegistrationMode(false);
        PasswordInput.Focus(FocusState.Programmatic);
    }
    async void ResendConfirmation(object sender, RoutedEventArgs args)
    {
        ResendConfirmationButton.IsEnabled = BackToLoginButton.IsEnabled = false;
        ConfirmationStatus.Visibility = Visibility.Visible;
        ConfirmationStatus.Text = "Sending…";
        try { await workspace.ResendConfirmation(ConfirmationAddress.Text); ConfirmationStatus.Text = "A new confirmation email was requested."; }
        catch (Exception error) { ConfirmationStatus.Text = error.Message; }
        finally { ResendConfirmationButton.IsEnabled = BackToLoginButton.IsEnabled = true; }
    }
    void Enter()
    {
        worker = new AgentWorker(); worker.Event += WorkerEvent;
        ConfigureWorkspaceWorker(worker);
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
        _ = SelectInitialModel();
        if (workspace.IsOfflineSession) progress.Text = "Offline · local workspace restored; account validation will retry.";
        sessionTimer.Start();
        accountSyncTimer.Start(); _ = SyncAccount(false);
        scheduleTimer.Start();
    }
    void InvokeNavigationAction(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, SignOutButton)) SignOut();
        else if (ReferenceEquals(args.InvokedItemContainer, UpdateButton)) UpdateAction();
        else if (activeSection != "settings" && !sectionChangedThisDispatch && args.InvokedItemContainer?.Tag as string == activeSection &&
            ContextBody.Content is not null) {
            contextOpen = !contextOpen;
            RefreshContextVisibility();
        }
    }

    async void SignOut()
    {
        if (workspace.AccountId.Length == 0 || !SignOutButton.IsEnabled) return;
        SignOutButton.IsEnabled = false;
        var account = workspace.AccountId;
        try {
            if (await Confirm("Sign out of Krono Agent?", "You’ll need to sign in again to open your workspace on this device.", "Sign out now") &&
                workspace.AccountId == account)
                await Leave(false);
        } catch (Exception error) { Error(error); }
        finally { SignOutButton.IsEnabled = true; }
    }
    void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (workspace.AccountId.Length == 0 || args.SelectedItem is not NavigationViewItem item) return;
        try {
            var section = item.Tag as string;
            if (section != activeSection) {
                activeSection = section ?? "";
                contextOpen = true;
                sectionChangedThisDispatch = true;
                DispatcherQueue.TryEnqueue(() => sectionChangedThisDispatch = false);
                ShowContext(null);
            }
            if (item.Tag?.ToString() != "chats") SetSideChatVisible(false);
            switch (item.Tag?.ToString()) {
                case "chats": ShowChats(); break;
                case "projects": ShowProjects(); break;
                case "settings": ShowSettings(); break;
                case "browser": browser ??= new BrowserWorkspace(workspace, this); DetachWorkspace(browser); KronoDesign.Present(Body, browser); break;
                case "terminal": terminal ??= new TerminalWorkspace(worker!); DetachWorkspace(terminal); KronoDesign.Present(Body, terminal); break;
                case "games": games ??= new GamesWorkspace(workspace); KronoDesign.Present(Body, games); break;
                case "schedule": ShowSchedules(); break;
            }
            UpdateActivityVisibility();
        } catch (Exception error) { Error(error); }
    }
    public void PauseGames(string reason) => games?.PauseAndSave(reason);
    async Task Leave(bool allDevices)
    {
        sessionGeneration++;
        StopSync();
        StopSchedules();
        ClearApprovalDetails();
        CloseSideChat();
        sessionTimer.Stop(); worker?.Dispose(); worker = null; browser?.Dispose(); browser = null; terminal?.Dispose(); terminal = null; games?.Dispose(); games = null;
        try { await workspace.Logout(allDevices); }
        finally {
            chatId = runId = loadedContext = approvalId = draftProject = null; Body.Content = null; chatList.ItemsSource = null; chatList.IsEnabled = true; RefreshComposerAction();
            collapsedChatProjects.Clear();
            expandedChatProjects.Clear();
            chatArchiveOpen = false;
            if (chatSearchInput is not null) chatSearchInput.Text = "";
            messages.Children.Clear(); conversationMap.Children.Clear(); composer.Text = progress.Text = "";
            streamTimer.Stop(); responseBuffer.Clear(); streaming = null; model = ""; RefreshChatModelLabel();
            splitWorkspace = null; SideBody.Content = null; SetSideChatVisible(false);
            runActions.Clear(); runChanges.Clear(); fileChanges = null; fileText = null;
            ClearAttachments();
            mode = "chat"; permissions = "ask"; if (modeSelector is not null) modeSelector.SelectedItem = mode; if (permissionSelector is not null) permissionSelector.SelectedItem = permissions;
            activityChatId = null; RunActivity.IsOpen = false; Approval.IsOpen = false;
            errorTimer?.Stop();
            if (errorPopup is not null) errorPopup.IsOpen = false;
            discoveredModels.Clear();
            Nav.Visibility = Visibility.Collapsed; SignIn.Visibility = Visibility.Visible;
        }
    }
    public async Task<bool> Confirm(string title, string message, string action = "Allow")
    {
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        KronoDesign.ApplyControlAccents(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    static TextBlock Heading(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
    Button Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => { try { action(); } catch (Exception error) { Error(error); } }; return button; }
    Button Button(string label, Func<Task> action) {
        var button = new Button { Content = label };
        var running = false;
        button.Click += async (_, _) => {
            if (running) return;
            running = true;
            try { await action(); } catch (Exception error) { Error(error); }
            finally { running = false; }
        };
        return button;
    }
    static ComboBox Choices(string label, string[] choices, string selected, Action<string> change)
    {
        var box = new KronoComboBox { Header = label, MinWidth = 120, ItemsSource = choices, SelectedItem = selected };
        AutomationProperties.SetAutomationId(box, label.Replace(" ", ""));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string value) change(value); }; return box;
    }
    static void MarkFullAccess(ComboBox control, string permissions)
    {
        if (permissions == "full") control.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 221, 120, 38));
        else control.ClearValue(Control.ForegroundProperty);
    }

    Button? backToChats;
    void ShowChats()
    {
        if (chatView is null) {
            chatView = new Grid();
            var history = new Grid { RowSpacing = 10, Width = 220 };
            history.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            history.RowDefinitions.Add(new RowDefinition());
            var header = new StackPanel { Spacing = 7 };
            chatSearchInput = new KronoTextBox { PlaceholderText = "Search chats", Visibility = Visibility.Collapsed };
            chatSearchInput.TextChanged += (_, _) => { if (workspace.AccountId.Length > 0) ReloadChats(); };
            var titleRow = new Grid { ColumnSpacing = 4 };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition());
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Heading("Chats");
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            var searchButton = HistoryAction("Search chats", KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Search), () => {
                var show = chatSearchInput.Visibility != Visibility.Visible;
                chatSearchInput.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) chatSearchInput.Focus(FocusState.Programmatic);
                else chatSearchInput.Text = "";
            });
            titleRow.Children.Add(searchButton);
            Grid.SetColumn(searchButton, 1);
            var projectsButton = HistoryAction("Projects", KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Folder), () => ShowProjects());
            titleRow.Children.Add(projectsButton);
            Grid.SetColumn(projectsButton, 2);
            header.Children.Add(titleRow);
            history.Children.Add(header);
            header.Children.Add(Button("+ New chat", () => {
                if (runId is not null) return;
                draftProject = chatId is null ? draftProject : workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).FirstOrDefault()?["project_id"]?.ToString();
                CloseSideChat();
                chatId = null;
                chatList.SelectedItem = null;
                if (!ReferenceEquals(Body.Content, chatView)) ShowChats();
                else LoadChat();
                composer.Focus(FocusState.Programmatic);
            }));
            InitializeChatRail();
            Grid.SetRow(chatList, 1);
            history.Children.Add(chatList);
            header.Children.Add(chatSearchInput);
            backToChats = Button("Back to chat", ShowChats);
            header.Children.Add(backToChats);
            chatHistory = history;
            var conversation = new Grid { RowSpacing = 12 }; chatView.Children.Add(conversation);
            foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) conversation.RowDefinitions.Add(new RowDefinition { Height = height });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            modeSelector = Choices("Mode", ["chat", "plan", "agent", "goal"], mode, value => mode = value); modeSelector.MinWidth = 110; toolbar.Children.Add(modeSelector);
            permissionSelector = Choices("Permissions", ["ask", "project", "full"], permissions, _ => { });
            permissionSelector.MinWidth = 110; toolbar.Children.Add(permissionSelector);
            var permissionBox = permissionSelector;
            permissionBox.SelectionChanged += async (_, _) => {
                if (permissionBox.SelectedItem is not string value) return;
                if (value == "full" && permissions != "full") {
                    permissionBox.SelectedItem = permissions;
                    permissionBox.IsEnabled = false;
                    var generation = sessionGeneration;
                    var selectedChat = chatId;
                    try {
                        if (!await Confirm("Enable full access?", "Tools can run commands and change files with your Windows permissions without asking.", "Enable full access") || generation != sessionGeneration || selectedChat != chatId) return;
                        permissions = "full";
                        permissionBox.SelectedItem = permissions;
                    } finally { permissionBox.IsEnabled = true; }
                    return;
                }
                permissions = value;
                MarkFullAccess(permissionBox, value);
            };
            ToolTipService.SetToolTip(permissionSelector, "Ask: approve changes/commands. Project: bounded file tools; no shell or desktop execution. Full: supplied tools run with your Windows account.");
            toolbar.Children.Add(Button("Project…", async () => {
                if (runId is not null) return;
                var target = chatId;
                var generation = sessionGeneration;
                var currentProject = target is null ? draftProject :
                    workspace.Query("SELECT project_id FROM chats WHERE id=$p0", target).FirstOrDefault()?["project_id"]?.ToString();
                var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name");
                var selector = new KronoComboBox {
                    MinWidth = 260,
                    ItemsSource = new[] { "No project" }.Concat(projects.Select(p => p["name"]!.ToString())).ToArray(),
                    SelectedIndex = projects.FindIndex(p => p["id"]?.ToString() == currentProject) + 1,
                };
                AutomationProperties.SetAutomationId(selector, "ChatProjectSelector");
                var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = target is null ? "Choose project for new chat" : "Move chat to project", Content = selector, PrimaryButtonText = target is null ? "Choose" : "Move", CloseButtonText = "Cancel" };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
                    generation != sessionGeneration || target != chatId || runId is not null) return;
                var project = selector.SelectedIndex > 0 ? projects[selector.SelectedIndex - 1]["id"]!.ToString() : null;
                if (project == currentProject) return;
                if (target is null) draftProject = project;
                else workspace.Execute("UPDATE chats SET project_id=$p0,updated_at=$p1 WHERE id=$p2 AND deleted_at IS NULL", project, Workspace.Now, target);
                LoadChat();
            }));
            var transcriptArea = new Grid();
            transcriptArea.ColumnDefinitions.Add(new ColumnDefinition());
            transcriptArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            transcriptArea.Children.Add(KronoDesign.ScrollPage(transcript));
            var map = new ScrollViewer { Content = conversationMap, HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            Grid.SetColumn(map, 1);
            transcriptArea.Children.Add(map);
            transcriptArea.SizeChanged += (_, args) => map.Visibility = args.NewSize.Width >= 430 ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(transcriptArea, 1); conversation.Children.Add(transcriptArea);
            var live = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { progressPulse, progress } };
            Grid.SetRow(live, 2); conversation.Children.Add(live);
            var input = new StackPanel { Spacing = 4 };
            input.Children.Add(attachmentTray);
            input.Children.Add(NativeMarkdown.Editor(composer));
            send.Click += async (_, _) => {
                if (!ComposerCanStop) await SendMessage();
                else if (scheduledRun is not null) await StopScheduledRun();
                else if (runId is { } activeRun) await worker!.Send(Guid.NewGuid().ToString(), "cancel", new { id = activeRun });
            };
            composer.TextChanged += (_, _) => RefreshComposerAction();
            var attach = Button("", PickAttachments);
            attach.Content = new SymbolIcon(Symbol.Add);
            AutomationProperties.SetName(attach, "Attach files…");
            ToolTipService.SetToolTip(attach, "Attach files…");
            send.Style = (Style)Application.Current.Resources["KronoPrimaryButton"];
            foreach (var action in new[] { attach, send }) {
                action.Width = 40;
                action.Padding = new Thickness(0);
                action.CornerRadius = new CornerRadius(10);
            }
            RefreshComposerAction();
            modelChoice.MaxWidth = 240;
            toolbar.Children.Add(currentModelLabel);
            toolbar.Children.Add(modelChoice);
            var toolsMenu = new MenuFlyout();
            foreach (var name in new[] { "Files", "Browser", "Terminal" }) {
                var entry = new MenuFlyoutItem { Text = name };
                entry.Click += (_, _) => OpenWorkspace(name);
                toolsMenu.Items.Add(entry);
            }
            var split = new Button { Content = "Workspace", Flyout = toolsMenu };
            toolbar.Children.Add(split);
            sideChatButton = Button("Side chat", OpenSideChat);
            AutomationProperties.SetAutomationId(sideChatButton, "OpenSideChatButton");
            toolbar.Children.Add(sideChatButton);
            foreach (var control in toolbar.Children.OfType<Control>()) control.VerticalAlignment = VerticalAlignment.Bottom;
            var actions = new Grid { ColumnSpacing = 12 };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.Children.Add(new ScrollViewer { Content = toolbar, HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Center });
            var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Bottom };
            void KeepClearOfPumpkin() =>
                primaryActions.Margin = new Thickness(0, 0, HalloweenPumpkin.Visibility == Visibility.Visible ? 16 : 0, 0);
            HalloweenPumpkin.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => KeepClearOfPumpkin());
            KeepClearOfPumpkin();
            primaryActions.Children.Add(attach);
            primaryActions.Children.Add(send);
            Grid.SetColumn(primaryActions, 1);
            actions.Children.Add(primaryActions);
            input.Children.Add(reasoningPanel);
            input.Children.Add(actions);
            Grid.SetRow(input, 3);
            conversation.Children.Add(input);
        }
        reasoningPanel.Visibility = Visibility.Collapsed;
        if (backToChats is not null) backToChats.Visibility = Visibility.Collapsed;
        ShowContext(chatHistory);
        RefreshChatModelLabel();
        KronoDesign.Present(Body, chatView); ReloadChats(); if (runId is null) LoadChat();
        SetSideChatVisible(splitWorkspace is not null || sideChat is not null && sideChat.ParentChatId == chatId);
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "chats");
        UpdateActivityVisibility();
    }
    bool ComposerCanStop => (runId is not null || scheduledRun is not null) && string.IsNullOrWhiteSpace(composer.Text);
    void RefreshComposerAction()
    {
        var canStop = ComposerCanStop;
        var label = canStop ? "Stop" : "Send";
        send.Content = new SymbolIcon(canStop ? Symbol.Stop : Symbol.Send);
        AutomationProperties.SetName(send, label);
        ToolTipService.SetToolTip(send, label);
        send.IsEnabled = canStop || (runId is null && scheduledRun is null);
        progressPulse.IsActive = runId is not null;
        progressPulse.Visibility = progressPulse.IsActive ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task DeleteChat(string id)
    {
        var account = workspace.AccountId;
        if (runId is not null || scheduledRun?.Chat == id) return;
        if (!await Confirm("Delete this chat?", "This conversation will be removed from your history.", "Delete chat")) return;
        if (workspace.AccountId != account || runId is not null || scheduledRun?.Chat == id) return;
        workspace.Execute("UPDATE chats SET deleted_at=$p0 WHERE id=$p1", Workspace.Now, id);
        if (chatId == id) chatId = null;
        ReloadChats();
        LoadChat();
    }

    Button HistoryAction(string name, FrameworkElement icon, Action action)
    {
        var button = new Button { Style = (Style)Resources["ChatRailIconButton"], Content = icon, Width = 28, Height = 28 };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    void LoadChat()
    {
        if (sideChatButton is not null)
            sideChatButton.Visibility = chatId is null ? Visibility.Collapsed : Visibility.Visible;
        if (sideChat is not null && sideChat.ParentChatId != chatId) CloseSideChat();
        if ((attachments.Count > 0 || pastedImage is not null) && attachmentChat != chatId) ClearAttachments();
        messages.Children.Clear();
        conversationMap.Children.Clear();
        if (chatId is null) {
            var welcome = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 520 };
            welcome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            welcome.ColumnDefinitions.Add(new ColumnDefinition());
            welcome.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center });
            var welcomeTitle = new TextBlock { Text = "What would you like to work on?", FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(welcomeTitle, 1);
            welcome.Children.Add(welcomeTitle);
            messages.Children.Add(welcome); return;
        }
        var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
        if (loadedContext != chatId + ":" + project) {
            loadedContext = chatId + ":" + project;
            var saved = workspace.GetChatOptions(chatId);
            permissions = saved?.Permissions ?? (project is null ? "ask" : workspace.ProjectPreferences(project)["permissions"]?.ToString() ?? "ask");
            if (permissionSelector is not null) permissionSelector.SelectedItem = permissions;
            if (saved is not null) {
                provider = saved.Provider; model = saved.Model; mode = saved.Mode;
                if (modeSelector is not null) modeSelector.SelectedItem = mode;
                RefreshChatModelLabel();
            }
        }
        foreach (var message in workspace.Query("SELECT id,role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY created_at,rowid", chatId)) AddBubble(message["role"]!.ToString(), message["content"]?.ToString() ?? "", message["id"]!.ToString());
    }
    void AddBubble(string role, string text, string messageId)
    {
        var box = new StackPanel { Spacing = 8 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new TextBlock { Text = role == "user" ? "You" : "Krono", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = .6, VerticalAlignment = VerticalAlignment.Center });
        if (role == "user") {
            var branch = Button("Branch", () => OpenSideChatFrom(messageId)); branch.Padding = new Thickness(8, 3, 8, 3); branch.FontSize = 11; branch.Tag = "caption";
            AutomationProperties.SetName(branch, "Side chat from this message"); header.Children.Add(branch);
        }
        box.Children.Add(header);
        box.Children.Add(Markdown(text));
        if (role != "user") AddResponseActions(box, text, messageId);
        FrameworkElement bubble = role == "user" ? new Border { Child = box, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 143, 215, 199)), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 143, 215, 199)), BorderThickness = new Thickness(1) } : box;
        messages.Children.Add(bubble);
        AddMapMark(bubble, text, role == "user");
    }
    RichTextBlock Markdown(string text) => NativeMarkdown.Render(text,
        KronoDesign.TextFont.Source, KronoDesign.TextSize, KronoDesign.CodeFont.Source, KronoDesign.CodeSize,
        workspace.Setting("native.codePalette"));
    async Task SendMessage()
    {
        if (runId is not null || scheduledRun is not null || (string.IsNullOrWhiteSpace(composer.Text) && attachments.Count == 0 && pastedImage is null)) return;
        var generation = sessionGeneration;
        try {
            var apiKey = ConnectionKey();
            var image = pastedImage;
            if (image is not null && provider != "openai") throw new InvalidOperationException("Choose an OpenAI model that supports images, or remove the pasted image.");
            var title = string.IsNullOrWhiteSpace(composer.Text) ? (image is null ? "Attached text files" : "Clipboard image") : composer.Text;
            var text = TextAttachment.Compose(composer.Text, attachments);
            if (image is not null) text = (text.Length == 0 ? "" : text + "\n\n") + "[Clipboard image attached]";
            chatId ??= workspace.CreateChat(draftProject);
            var reasoning = workspace.Setting(ChatReasoningKey);
            var context = workspace.Setting("native.chatContext");
            var fastMode = workspace.Setting(ChatFastModeKey) == "true";
            workspace.SaveChatOptions(chatId, new(provider, model, mode, permissions, reasoning, context, fastMode));
            var messageId = workspace.AddMessage(chatId, "user", text);
            composer.Text = ""; ClearAttachments();
            if (workspace.Query("SELECT id FROM messages WHERE chat_id=$p0", chatId).Count == 1) workspace.Execute("UPDATE chats SET name=$p0 WHERE id=$p1", title[..Math.Min(42, title.Length)], chatId);
            LoadChat(); ReloadChats(); streaming = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["KronoTextFont"], FontSize = (double)Application.Current.Resources["KronoTextSize"], LineHeight = 24 }; messages.Children.Add(streaming);
            responseBuffer.Clear(); streamDirty = false; streamTimer.Start(); BeginRunSummary();
            var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
            var dirs = project is null ? [] : workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", project).Select(p => p["path"]!.ToString()).ToArray();
            var memories = workspace.Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", project).Select(p => p["content"]!.ToString()).ToArray();
            runId = Guid.NewGuid().ToString(); RefreshComposerAction(); chatList.IsEnabled = false; progress.Text = "Preparing response…";
            activityChatId = chatId; RunActivity.Title = "Agent running"; RunActivity.Message = workspace.Query("SELECT name FROM chats WHERE id=$p0", chatId).First()["name"]?.ToString(); RunActivity.IsClosable = false; UpdateActivityVisibility();
            ReloadChats();
            var history = workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY id=$p1,created_at,rowid", chatId, messageId).Select(p => new { role = p["role"]!.ToString(), content = p["content"]!.ToString() }).ToArray();
            if (workspace.Setting("native.chatContext") == "Recent messages") history = history.TakeLast(20).ToArray();
            var request = JsonSerializer.SerializeToNode(new { providerId = provider, apiKey, endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled, modelId = model, mode, permissions, messages = history, projectDirs = dirs, instructions = workspace.Instructions(project), memories, fastMode,
                plugins = PluginCatalog.Enabled(workspace, (mode is "agent" or "goal") && ProviderToolsEnabled) })!.AsObject();
            if (image is not null) request["messages"]!.AsArray()[^1]!["imagePng"] = image;
            if (IsReasoningEffort(reasoning)) request["reasoningEffort"] = reasoning;
            await worker!.Request(runId, "run", request);
            if (generation != sessionGeneration) return;
            SaveRunSummary(workspace.AddMessage(chatId, "assistant", responseBuffer.ToString())); progress.Text = "";
            RunActivity.Title = "Response ready"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility();
        } catch (Exception error) { if (generation == sessionGeneration) { Error(error); progress.Text = "Stopped"; RunActivity.Title = "Agent stopped"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility(); } }
        finally { if (generation == sessionGeneration) { streamTimer.Stop(); streaming = null; runId = null; RefreshComposerAction(); chatList.IsEnabled = true; Approval.IsOpen = false; approvalId = null; ClearApprovalDetails(); if (workspace.AccountId.Length > 0) { workspace.FlushSideTransfers(); LoadChat(); ReloadChats(); } } }
    }
    void WorkerEvent(string id, string type, JsonNode? data) {
        ReceiveAgentWorkspace(id, type, data);
        if (id != runId) return;
        if (type == "approval" && data is not null) DisplayApproval(data);
        if (type == "chunk" && data?["type"]?.ToString() == "text") { responseBuffer.Append(data["content"]?.ToString()); streamDirty = true; }
        if (type == "chunk" && data?["type"]?.ToString() == "tool_call") { progress.Text = "Using " + data["toolCall"]?["name"]; RunActivity.Title = progress.Text; }
    }
    void DisplayApproval(JsonNode data) { approvalId = data["id"]!.ToString(); approvalDetails = data["details"]?.DeepClone(); var target = data["target"]?.ToString() ?? ""; Approval.Message = data["action"] + " · " + (target.Length > 240 ? target[..240] + "…" : target); Approval.IsOpen = true; RunActivity.Title = "Approval needed"; }
    void UpdateActivityVisibility() => RunActivity.IsOpen = activityChatId is not null && (!ReferenceEquals(Body.Content, chatView) || activityChatId != chatId);
    void ActivityClosed(InfoBar sender, InfoBarClosedEventArgs args) { if (runId is null && scheduledRun is null) activityChatId = null; }
    void OpenActivityChat(object sender, RoutedEventArgs args) {
        try { if (activityChatId is null) return; var id = activityChatId; games?.PauseForAgent(); OpenNormalChat(id); if (runId is null && scheduledRun is null) activityChatId = null; UpdateActivityVisibility(); }
        catch (Exception error) { Error(error); }
    }
    async void Approve(object sender, RoutedEventArgs args) { try { await AnswerApproval(true); } catch (Exception error) { Error(error); } }
    async void Deny(object sender, RoutedEventArgs args) { try { await AnswerApproval(false); } catch (Exception error) { Error(error); } }
    async Task AnswerApproval(bool approved) { if (approvalId is not { } id) return; await (scheduledWorker ?? worker)!.Send(Guid.NewGuid().ToString(), "approve", new { id, approved }); if (approvalId != id) return; approvalId = null; ClearApprovalDetails(); Approval.IsOpen = false; }
    void ClearApprovalDetails() { if (approvalDialog?.Content is TextBox content) content.Text = ""; approvalDialog?.Hide(); approvalDialog = null; approvalDetails = null; }
    async void ReviewApproval(object sender, RoutedEventArgs args) {
        if (approvalDetails is null || approvalDialog is not null) return;
        var details = new KronoTextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 350, MaxHeight = 480, FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize, Text = approvalDetails.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) };
        AutomationProperties.SetAutomationId(details, "ToolApprovalDetails");
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = "Action details", Content = details, CloseButtonText = "Close details" }; approvalDialog = dialog;
        try { await dialog.ShowAsync(); } catch (Exception error) { Error(error); } finally { if (ReferenceEquals(approvalDialog, dialog)) approvalDialog = null; }
    }
    int themeTransition;
    async void ApplyTheme(string value, bool animate = false)
    {
        var transition = ++themeTransition;
        var session = sessionGeneration;
        if (animate) await KronoDesign.BeginThemeChange(this);
        if (transition != themeTransition) return;
        if (session != sessionGeneration) { KronoDesign.FinishThemeChange(this); return; }
        KronoDesign.ApplyPalette(value == "halloween");
        RequestedTheme = value == "light" ? ElementTheme.Light : value is "dark" or "halloween" ? ElementTheme.Dark : ElementTheme.Default;
        if (MainWindow.Instance?.Content is FrameworkElement root) root.RequestedTheme = RequestedTheme;
        HalloweenPumpkin.Visibility = HalloweenCorner.Visibility = value == "halloween" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var backdrop in WorkspacePattern.Children.OfType<GameBackdrop>().Concat(LoginPattern.Children.OfType<GameBackdrop>()))
            backdrop.SetHalloween(value == "halloween");
        MainWindow.Instance?.ShowHalloween(value == "halloween");
        KronoInputMotion.RefreshPalette(this);
        KronoDesign.FinishThemeChange(this);
    }
    bool CompatibleEndpoint => ProviderCatalog.Compatible(provider);
    string ProviderEndpoint => workspace.Setting("native.endpoint." + provider) is { Length: > 0 } endpoint ? endpoint : ProviderCatalog.Endpoint(provider);
    string ModelToolsSetting => "native.tools." + provider + "." + model;
    bool ProviderToolsEnabled => !CompatibleEndpoint || model.Length > 0 && workspace.Setting(ModelToolsSetting) == "true";
    string ConnectionKey() {
        if (model.Length == 0) throw new InvalidOperationException("Choose a provider and model in Settings first.");
        if (ProviderCatalog.AutomaticLocal(provider)) return "";
        var endpoint = CompatibleEndpoint ? ProviderCatalog.ParseEndpoint(ProviderEndpoint) : null;
        var key = workspace.Key(provider);
        if (key.Length == 0 && (ProviderCatalog.RequiresKey(provider) || endpoint is not null && !ProviderCatalog.Local(endpoint))) throw new InvalidOperationException("Save this provider's API key in Settings first.");
        return key;
    }
    void ShowSettings()
    {
        var pages = new Dictionary<string, StackPanel>();
        StackPanel AddCategory(string name)
        {
            var section = new StackPanel { Spacing = 16, MaxWidth = 650, HorizontalAlignment = HorizontalAlignment.Left };
            section.Children.Add(Heading(name));
            pages.Add(name, section);
            return section;
        }
        var panel = AddCategory("Account");
        panel.Children.Add(new TextBlock { Text = workspace.Email, Opacity = .65 });
        var googleStatus = new TextBlock { Opacity = .65, TextWrapping = TextWrapping.Wrap };
        var googleLink = Button("Link Google account", async () => {
            try { if (await AuthorizeGoogle(true)) ShowSettings(); }
            catch (Exception error) { Error(error); }
        });
        if (workspace.GoogleEmail is { } google) {
            googleStatus.Text = "Google account: " + google;
            panel.Children.Add(googleStatus);
        } else panel.Children.Add(googleLink);
        panel.Children.Add(Button("Sign out on all devices", async () => { if (await Confirm("Sign out on all devices?", "You’ll need to sign in again on each device.", "Sign out everywhere")) await Leave(true); }));
        AddSyncSettings(panel);
        panel = AddCategory("Models");
        var secret = new PasswordBox { Header = "Provider API key", HorizontalAlignment = HorizontalAlignment.Stretch };
        KronoInputMotion.Attach(secret, "KronoPasswordInput");
        var endpoint = new KronoTextBox { Header = "Server base URL · include /v1", Text = ProviderEndpoint };
        AutomationProperties.SetAutomationId(endpoint, "ProviderEndpoint");
        var tools = new KronoCheckBox { Content = "Enable tool calling for this model", IsChecked = ProviderToolsEnabled };
        AutomationProperties.SetAutomationId(tools, "ProviderTools");
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7 }; AutomationProperties.SetAutomationId(connectionStatus, "ProviderConnectionStatus");
        var models = new KronoComboBox { Header = "Model", HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Choose an available model", ItemsSource = ModelChoices(provider, [], model, false), SelectedItem = model };
        var profileHelp = new TextBlock { Text = ProviderCatalog.Help(provider), TextWrapping = TextWrapping.Wrap, Opacity = .65 };
        var modelSettings = new StackPanel { Spacing = 8 };
        var remoteConnection = new StackPanel { Spacing = 10 };
        var modelStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var advanced = new Expander { Header = "Advanced connection settings", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(advanced, "ProviderConnectionOptions");
        var connectionFields = new StackPanel { Spacing = 10 };
        advanced.Content = connectionFields;
        int modelRequest = 0;
        var saveKey = Button("Save key", SaveKey);
        void SaveEndpoint() { try {
            var url = ProviderCatalog.ParseEndpoint(endpoint.Text);
            workspace.SetSetting("native.endpoint." + provider, url.AbsoluteUri.TrimEnd('/')); connectionStatus.Text = "Endpoint saved.";
            discoveredModels.Remove(provider);
            _ = LoadProviderModels();
        } catch (Exception error) { Error(error); } }
        var saveEndpoint = Button("Save endpoint", SaveEndpoint);
        endpoint.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; SaveEndpoint(); } };
        void RefreshModelVisibility()
        {
            modelRequest++;
            modelStatus.Text = "";
            modelSettings.Visibility = !ProviderCatalog.RequiresKey(provider) || workspace.Key(provider).Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void EndpointVisibility()
        {
            var automatic = ProviderCatalog.AutomaticLocal(provider);
            endpoint.Visibility = saveEndpoint.Visibility = CompatibleEndpoint && !automatic ? Visibility.Visible : Visibility.Collapsed;
            secret.Visibility = saveKey.Visibility = automatic ? Visibility.Collapsed : Visibility.Visible;
            remoteConnection.Visibility = automatic ? Visibility.Collapsed : Visibility.Visible;
            advanced.Visibility = !automatic || model.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            advanced.IsExpanded = !automatic && ProviderCatalog.RequiresKey(provider) && workspace.Key(provider).Length == 0;
            tools.Visibility = CompatibleEndpoint ? Visibility.Visible : Visibility.Collapsed;
            tools.IsEnabled = model.Length > 0;
            RefreshModelVisibility();
        }
        ToolTipService.SetToolTip(tools, "Override tool support for a custom provider and model.");
        var updatingModels = false;
        var providerIds = ProviderCatalog.Ids; var providerNames = providerIds.Select(ProviderCatalog.Name).ToArray();
        panel.Children.Add(Choices("Provider", providerNames, ProviderCatalog.Name(provider), value => {
            provider = providerIds[Array.IndexOf(providerNames, value)];
            workspace.SetSetting("native.provider", provider);
            secret.Password = "";
            model = workspace.Setting("native.model." + provider);
            updatingModels = true;
            models.ItemsSource = ModelChoices(provider, [], model, false);
            models.SelectedItem = model;
            updatingModels = false;
            RefreshChatModelLabel();
            endpoint.Text = ProviderEndpoint;
            tools.IsChecked = ProviderToolsEnabled;
            connectionStatus.Text = "";
            profileHelp.Text = ProviderCatalog.Help(provider);
            EndpointVisibility();
            _ = LoadProviderModels();
        }));
        tools.Checked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "true"); }; tools.Unchecked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "false"); };
        EndpointVisibility();
        connectionFields.Children.Add(remoteConnection);
        remoteConnection.Children.Add(endpoint); remoteConnection.Children.Add(saveEndpoint); remoteConnection.Children.Add(secret);
        AutomationProperties.SetAutomationId(secret, "ProviderKey");
        void SaveKey()
        {
            if (string.IsNullOrWhiteSpace(secret.Password)) return;
            try { workspace.SaveKey(provider, secret.Password.Trim()); secret.Password = ""; discoveredModels.Remove(provider); RefreshModelVisibility(); _ = LoadProviderModels(); }
            catch (Exception error) { Error(error); }
        }
        secret.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; SaveKey(); } };
        remoteConnection.Children.Add(saveKey);
        remoteConnection.Children.Add(Button("Test connection", async () => {
            var id = provider;
            var key = workspace.Key(id);
            if (key.Length == 0 && (ProviderCatalog.RequiresKey(id) || CompatibleEndpoint && !ProviderCatalog.Local(ProviderCatalog.ParseEndpoint(ProviderEndpoint))))
                throw new InvalidOperationException("Save the provider’s API key before testing the connection.");
            var result = await worker!.Request(Guid.NewGuid().ToString(), "test", new { providerId = id, apiKey = key, endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled });
            if (provider != id) return;
            if (result?["success"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Connection failed. Check the server, endpoint and credentials.");
            connectionStatus.Text = "Connected · " + result["latencyMs"] + " ms";
        }));
        remoteConnection.Children.Add(connectionStatus);
        remoteConnection.Children.Add(profileHelp);
        connectionFields.Children.Add(tools);
        panel.Children.Add(modelSettings);
        modelSettings.Children.Add(models);
        AutomationProperties.SetAutomationId(models, "ModelSelector");
        modelSettings.Children.Add(Button("Refresh models", () => LoadProviderModels(true)));
        modelSettings.Children.Add(modelStatus);
        panel.Children.Add(advanced);
        async Task LoadProviderModels(bool refresh = false)
        {
            var selectedProvider = provider;
            var key = workspace.Key(selectedProvider);
            if (key.Length == 0 && ProviderCatalog.RequiresKey(selectedProvider) || worker is null) return;
            var request = ++modelRequest;
            var generation = sessionGeneration;
            modelStatus.Text = "Loading models…";
            try {
                var result = await DiscoverModels(selectedProvider, refresh);
                if (request != modelRequest || generation != sessionGeneration || provider != selectedProvider) return;
                ChooseDefaultModel(result);
                var ids = ModelChoices(selectedProvider, result, model, false);
                updatingModels = true;
                try {
                    models.ItemsSource = ids;
                    models.SelectedItem = model;
                    models.IsEnabled = ids.Length > 0;
                    tools.IsChecked = ProviderToolsEnabled;
                    tools.IsEnabled = model.Length > 0;
                    if (ProviderCatalog.AutomaticLocal(selectedProvider)) advanced.Visibility = model.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                } finally { updatingModels = false; }
                modelStatus.Text = ids.Length == 0
                    ? ProviderCatalog.AutomaticLocal(selectedProvider) ? "No local models found. Start your model server and refresh." : "This provider returned no models."
                    : "";
            } catch (Exception) {
                if (request == modelRequest && generation == sessionGeneration && provider == selectedProvider)
                    modelStatus.Text = "Could not load this provider’s models.";
            }
        }
        models.SelectionChanged += (_, _) => {
            if (updatingModels || models.SelectedItem is not string selected) return;
            SelectChatModel(selected);
            tools.IsChecked = ProviderToolsEnabled;
            tools.IsEnabled = true;
        };
        panel = AddCategory("Appearance");
        panel.Children.Add(Choices("Appearance", ["system", "dark", "light", "halloween"], workspace.Setting("native.theme") is { Length: > 0 } t ? t : "system", value => { workspace.SetSetting("native.theme", value); ApplyTheme(value, animate: true); }));
        KronoComboBox FontChoice(string header, string key, string[] choices) {
            var value = workspace.Setting(key) is { Length: > 0 } saved ? saved : choices[0];
            return new KronoComboBox { Header = header, ItemsSource = choices.Append(value).Distinct().ToArray(),
                SelectedItem = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        }
        var textFont = FontChoice("Text font", "native.textFont", ["Rubik", "Space Grotesk", "Segoe UI", "Segoe UI Variable", "Arial", "Verdana", "Georgia", "Tahoma", "Trebuchet MS"]);
        var codeFont = FontChoice("Code font", "native.codeFont", ["JetBrains Mono", "Cascadia Code", "Cascadia Mono", "Consolas", "Courier New", "Lucida Console"]);
        AutomationProperties.SetAutomationId(textFont, "TextFontFamily"); AutomationProperties.SetAutomationId(codeFont, "CodeFontFamily");
        var textSize = new NumberBox { Header = "Text size", Minimum = 11, Maximum = 28, Value = KronoDesign.TextSize };
        var codeSize = new NumberBox { Header = "Code size", Minimum = 10, Maximum = 26, Value = KronoDesign.CodeSize };
        AutomationProperties.SetAutomationId(textSize, "TextFontSize"); AutomationProperties.SetAutomationId(codeSize, "CodeFontSize");
        AddFontRow(textFont, textSize);
        AddFontRow(codeFont, codeSize);
        void AddFontRow(KronoComboBox family, NumberBox size)
        {
            KronoInputMotion.Attach(size);
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            size.Width = 110;
            Grid.SetColumn(size, 1);
            row.Children.Add(family);
            row.Children.Add(size);
            panel.Children.Add(row);
        }
        panel.Children.Add(Choices("Code palette", ["classic", "mint", "halloween"], workspace.Setting("native.codePalette") is { Length: > 0 } palette ? palette : "classic", value => workspace.SetSetting("native.codePalette", value)));
        void SaveTypography() {
            workspace.SetSetting("native.textFont", textFont.SelectedItem?.ToString() ?? "Rubik");
            workspace.SetSetting("native.codeFont", codeFont.SelectedItem?.ToString() ?? "JetBrains Mono");
            workspace.SetSetting("native.textSize", (double.IsFinite(textSize.Value) ? Math.Clamp(textSize.Value, 11, 28) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            workspace.SetSetting("native.codeSize", (double.IsFinite(codeSize.Value) ? Math.Clamp(codeSize.Value, 10, 26) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            KronoDesign.ApplyTypography(workspace, this);
        }
        textFont.SelectionChanged += (_, _) => SaveTypography();
        codeFont.SelectionChanged += (_, _) => SaveTypography();
        textSize.ValueChanged += (_, _) => SaveTypography();
        codeSize.ValueChanged += (_, _) => SaveTypography();
        var instructions = BuildInstructionsSettings();
        AddCategory("Plugins");
        AddUpdateSettings(AddCategory("Updates"));
        var names = pages.Keys.ToList();
        names.Insert(names.IndexOf("Plugins"), "Instructions");
        var categories = new ListView { ItemsSource = names, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetAutomationId(categories, "SettingsCategories");
        var sidebar = new StackPanel { Spacing = 4 };
        sidebar.Children.Add(Heading("Settings"));
        sidebar.Children.Add(categories);
        ShowContext(sidebar);
        var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var views = pages.ToDictionary(entry => entry.Key, entry => KronoDesign.ScrollPage(entry.Value));
        views.Add("Instructions", instructions);
        var pluginsLoaded = false;
        var modelsLoaded = false;
        categories.SelectionChanged += (_, _) => {
            if (categories.SelectedItem is not string selected) return;
            if (selected == "Models" && !modelsLoaded) {
                modelsLoaded = true;
                _ = LoadProviderModels();
            }
            if (selected == "Plugins" && !pluginsLoaded) {
                pluginsLoaded = true;
                AddPluginSettings(pages[selected]);
            }
            KronoDesign.Present(page, views[selected]);
        };
        categories.SelectedIndex = 0;
        KronoDesign.Present(Body, page);
    }
}
