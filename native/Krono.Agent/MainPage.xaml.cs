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
    Button? chatProjectButton;
    JsonNode? approvalDetails;
    ContentDialog? approvalDialog;
    string? activityChatId;
    string provider = "openai", model = "", mode = "chat", permissions = "ask";
    readonly ListView chatList = new() { Width = 220, SelectionMode = ListViewSelectionMode.Single };
    readonly StackPanel messages = new() { Spacing = 20, MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
    readonly Grid welcomeChat = new() { ColumnSpacing = 12, MaxWidth = 520, Margin = new Thickness(20, 0, 20, 0),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    readonly KronoTextBox composer = new() { PlaceholderText = "Message Krono…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 180 };
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
        Unloaded += (_, _) => { HideError(); confirmationWait?.Cancel(); sessionGeneration++; sessionTimer.Stop(); StopSync(); StopSchedules(); sideChat?.Dispose(); worker?.Dispose(); browser?.Dispose(); terminal?.Dispose(); games?.Dispose(); workspace.Dispose(); };
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
    void StyleAuthPassword(object sender, RoutedEventArgs args)
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
        try { await workspace.ResendConfirmation(ConfirmationAddress.Text); ShowNotice("A new confirmation email was requested."); }
        catch (Exception error) { Error(error); }
        finally {
            ConfirmationStatus.Visibility = Visibility.Collapsed;
            ResendConfirmationButton.IsEnabled = BackToLoginButton.IsEnabled = true;
        }
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
        PluginCatalog.RestoreInstallations(workspace);
        workspace.FlushSideTransfers(); ShowChats();
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
                case "browser": browser ??= new BrowserWorkspace(workspace, this); DetachWorkspace(browser); browser.SetBrowserHost(split: false); KronoDesign.Present(Body, browser); break;
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
            HideError();
            discoveredModels.Clear();
            Nav.Visibility = Visibility.Collapsed; SignIn.Visibility = Visibility.Visible;
        }
    }
    public async Task<bool> Confirm(string title, string message, string action = "Allow", bool destructive = false)
    {
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, DestructivePrimary = destructive };
        if (destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["KronoButton"];
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
            var history = new Grid { RowSpacing = 14, Width = 220 };
            history.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            history.RowDefinitions.Add(new RowDefinition());
            var header = new StackPanel { Spacing = 10 };
            chatSearchInput = new KronoTextBox { PlaceholderText = "Search chats", Visibility = Visibility.Collapsed };
            chatSearchInput.TextChanged += (_, _) => { if (workspace.AccountId.Length > 0) ReloadChats(); };
            var titleRow = new Grid { ColumnSpacing = 8 };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition());
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Heading("Chats");
            title.Margin = new Thickness(0);
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
            header.Children.Add(chatSearchInput);
            history.Children.Add(header);
            header.Children.Add(Button("+ New chat", () => {
                if (runId is not null) return;
                draftProject = chatId is null ? draftProject : workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).FirstOrDefault()?["project_id"]?.ToString();
                CloseSideChat();
                chatId = null;
                chatList.SelectedItem = null;
                if (!ReferenceEquals(Body.Content, chatView)) ShowChats();
                else LoadChat();
                NativeMarkdown.Focus(composer);
            }));
            InitializeChatRail();
            Grid.SetRow(chatList, 1);
            history.Children.Add(chatList);
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
            chatProjectButton = Button("Project…", () => ChooseChatProject(chatId));
            AutomationProperties.SetAutomationId(chatProjectButton, "ChatProjectButton");
            toolbar.Children.Add(chatProjectButton);
            var transcriptArea = new Grid();
            transcriptArea.ColumnDefinitions.Add(new ColumnDefinition());
            transcriptArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            transcriptArea.Children.Add(KronoDesign.ScrollPage(transcript));
            transcriptArea.Children.Add(welcomeChat);
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
            input.Children.Add(NativeMarkdown.Editor(composer, workspace, TryPasteComposerImage));
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
            input.Children.Add(actions);
            Grid.SetRow(input, 3);
            conversation.Children.Add(input);
        }
        reasoningFlyout?.Hide();
        if (backToChats is not null) backToChats.Visibility = Visibility.Collapsed;
        ShowContext(chatHistory);
        RefreshChatModelLabel();
        KronoDesign.Present(Body, chatView); ReloadChats(); if (runId is null) LoadChat();
        SetSideChatVisible(splitWorkspace is not null || sideChat is not null && sideChat.ParentChatId == chatId);
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "chats");
        UpdateActivityVisibility();
        _ = SelectInitialModel();
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
        if (!await Confirm("Delete this chat?", "This conversation will be removed from your history.", "Delete chat", destructive: true)) return;
        if (workspace.AccountId != account || runId is not null || scheduledRun?.Chat == id) return;
        workspace.Execute("UPDATE chats SET deleted_at=$p0 WHERE id=$p1", Workspace.Now, id);
        if (chatId == id) chatId = null;
        ReloadChats();
        LoadChat();
    }

    Button HistoryAction(string name, FrameworkElement icon, Action action)
    {
        var button = new Button { Content = icon, Width = 28, Height = 28 };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        KronoInputMotion.StyleButton(button);
        button.Click += (_, _) => action();
        return button;
    }

    void LoadChat()
    {
        var project = chatId is null ? draftProject :
            workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).FirstOrDefault()?["project_id"]?.ToString();
        if (chatProjectButton is not null)
            chatProjectButton.Content = (project is null ? null : workspace.Query(
                "SELECT name FROM projects WHERE id=$p0 AND deleted_at IS NULL", project).FirstOrDefault()?["name"]?.ToString()) ?? "Project…";
        if (sideChatButton is not null)
            sideChatButton.Visibility = chatId is null ? Visibility.Collapsed : Visibility.Visible;
        if (sideChat is not null && sideChat.ParentChatId != chatId) CloseSideChat();
        if ((attachments.Count > 0 || pastedImage is not null) && attachmentChat != chatId) ClearAttachments();
        messages.Children.Clear();
        conversationMap.Children.Clear();
        welcomeChat.Visibility = chatId is null ? Visibility.Visible : Visibility.Collapsed;
        if (chatId is null) {
            if (welcomeChat.Children.Count > 0) return;
            var welcome = welcomeChat;
            welcome.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            welcome.ColumnDefinitions.Add(new ColumnDefinition());
            welcome.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Krono.svg")), Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center });
            var welcomeTitle = new TextBlock { Text = "What would you like to work on?", FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(welcomeTitle, 1);
            welcome.Children.Add(welcomeTitle);
            return;
        }
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
        if (role != "user") AddCompletedWork(box, messageId);
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
            var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
            if (project is not null) workspace.SetSetting("native.lastProjectPermission." + project, permissions);
            var messageId = workspace.AddMessage(chatId, "user", text);
            composer.Text = ""; ClearAttachments();
            if (workspace.Query("SELECT id FROM messages WHERE chat_id=$p0", chatId).Count == 1) workspace.Execute("UPDATE chats SET name=$p0 WHERE id=$p1", title[..Math.Min(42, title.Length)], chatId);
            LoadChat(); ReloadChats(); streaming = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["KronoTextFont"], FontSize = (double)Application.Current.Resources["KronoTextSize"], LineHeight = 24 }; messages.Children.Add(streaming);
            responseBuffer.Clear(); streamDirty = false; streamTimer.Start(); BeginRunSummary();
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
    bool ProviderToolsEnabled => !CompatibleEndpoint || workspace.Setting(ModelToolsSetting) != "false";
    string ConnectionKey() {
        if (model.Length == 0) throw new InvalidOperationException("Choose a provider and model in Settings first.");
        if (ProviderCatalog.AutomaticLocal(provider)) return "";
        var endpoint = CompatibleEndpoint ? ProviderCatalog.ParseEndpoint(ProviderEndpoint) : null;
        var key = workspace.Key(provider);
        if (key.Length == 0 && ProviderCatalog.RequiresKey(provider, endpoint?.AbsoluteUri ?? "")) throw new InvalidOperationException("Save this provider's API key in Settings first.");
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
        var modelPanel = panel;
        var secret = new PasswordBox { Header = "API key", HorizontalAlignment = HorizontalAlignment.Stretch };
        KronoInputMotion.Attach(secret, "KronoPasswordInput");
        AutomationProperties.SetAutomationId(secret, "ProviderKey");
        var models = new KronoComboBox { Header = "Model", HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Choose an available model", ItemsSource = ModelChoices(provider, [], model, false), SelectedItem = model };
        AutomationProperties.SetAutomationId(models, "ModelSelector");
        int modelRequest = 0;
        bool updatingModels = false, loadingModels = false;
        var saveKey = Button("Save key", SaveKey);
        var optionalKey = Button("Configure optional server token", () => {
            secret.Visibility = saveKey.Visibility = Visibility.Visible;
            secret.Focus(FocusState.Programmatic);
        });
        void ShowKeyField()
        {
            var automatic = ProviderCatalog.AutomaticLocal(provider);
            var required = ProviderCatalog.RequiresKey(provider, ProviderEndpoint);
            secret.Header = required ? "API key" : "Server token (optional)";
            secret.Visibility = saveKey.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
            optionalKey.Visibility = !automatic && !required ? Visibility.Visible : Visibility.Collapsed;
        }
        void ListModels(string[] ids)
        {
            updatingModels = true;
            try { models.ItemsSource = ids; models.SelectedItem = model; }
            finally { updatingModels = false; }
        }
        var providerIds = ProviderCatalog.Ids.Where(id => !ProviderCatalog.Compatible(id) ||
            ProviderCatalog.AutomaticLocal(id) || ProviderCatalog.Endpoint(id).Length > 0 ||
            workspace.Setting("native.endpoint." + id).Length > 0 || id == provider).ToArray();
        var providerNames = providerIds.Select(ProviderCatalog.Name).ToArray();
        panel.Children.Add(Choices("Provider", providerNames, ProviderCatalog.Name(provider), value => {
            provider = providerIds[Array.IndexOf(providerNames, value)];
            workspace.SetSetting("native.provider", provider);
            secret.Password = "";
            model = workspace.Setting("native.model." + provider);
            ListModels(ModelChoices(provider, [], model, false));
            RefreshChatModelLabel();
            ShowKeyField();
            _ = LoadProviderModels();
        }));
        panel.Children.Add(models);
        panel.Children.Add(optionalKey);
        panel.Children.Add(secret);
        panel.Children.Add(saveKey);
        ShowKeyField();
        void SaveKey()
        {
            if (string.IsNullOrWhiteSpace(secret.Password)) {
                ShowNotice("Enter an API key before saving.", success: false);
                return;
            }
            try {
                workspace.SaveKey(provider, secret.Password.Trim());
                secret.Password = "";
                ShowNotice(ProviderCatalog.Name(provider) + " API key saved.");
                _ = LoadProviderModels(refresh: true);
            } catch (Exception error) { Error(error); }
        }
        secret.PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Enter || args.KeyStatus.WasKeyDown) return;
            args.Handled = true;
            SaveKey();
        };
        async Task LoadProviderModels(bool refresh = false)
        {
            var request = ++modelRequest;
            loadingModels = false;
            var selectedProvider = provider;
            var generation = sessionGeneration;
            if (workspace.Key(selectedProvider).Length == 0 && ProviderCatalog.RequiresKey(selectedProvider, ProviderEndpoint)) {
                SetModelStatus("Save an API key to load models");
                return;
            }
            if (worker is null) return;
            loadingModels = true;
            SetModelStatus("Loading models…");
            bool Current() => request == modelRequest && generation == sessionGeneration && provider == selectedProvider;
            try {
                var result = await DiscoverModels(selectedProvider, refresh);
                if (!Current()) return;
                ChooseDefaultModel(result);
                var ids = ModelChoices(selectedProvider, result, model, false);
                ListModels(ids);
                SetModelStatus(ids.Length == 0 ? "No models available" : "Choose an available model");
            } catch (Exception) {
                if (Current()) SetModelStatus("Could not load models");
            } finally { if (Current()) loadingModels = false; }
        }
        void SetModelStatus(string status)
        {
            models.PlaceholderText = status;
            models.EmptyText = status;
        }
        models.DropDownOpened += (_, _) => {
            if (!loadingModels && models.Items.Count == 0) _ = LoadProviderModels(refresh: true);
        };
        models.SelectionChanged += (_, _) => {
            if (!updatingModels && models.SelectedItem is string selected) SelectChatModel(selected);
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
        var textSize = FontSizeField("Text size", KronoDesign.TextSize);
        var codeSize = FontSizeField("Code size", KronoDesign.CodeSize);
        AutomationProperties.SetAutomationId(textSize, "TextFontSize"); AutomationProperties.SetAutomationId(codeSize, "CodeFontSize");
        AddFontRow(textFont, textSize);
        AddFontRow(codeFont, codeSize);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        KronoTextBox FontSizeField(string title, double value) => new() {
            Header = title, Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MinHeight = 32, Padding = new Thickness(12, 4, 12, 4),
            Style = (Style)Application.Current.Resources["KronoCompactTextInput"],
            InputScope = new Microsoft.UI.Xaml.Input.InputScope {
                Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) }
            }
        };
        void AddFontRow(KronoComboBox family, KronoTextBox size)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            size.Width = 110;
            var familyField = LabeledField(family, family.Header?.ToString());
            var sizeField = LabeledField(size, size.Header?.ToString());
            family.Header = size.Header = null;
            Grid.SetColumn(sizeField, 1);
            row.Children.Add(familyField);
            row.Children.Add(sizeField);
            panel.Children.Add(row);

            static StackPanel LabeledField(Control field, string? label)
            {
                var title = new TextBlock { Text = label };
                AutomationProperties.SetLabeledBy(field, title);
                field.Height = field.MinHeight = 32;
                field.VerticalContentAlignment = VerticalAlignment.Center;
                return new StackPanel { Spacing = 8, Children = { title, field } };
            }
        }
        panel.Children.Add(Choices("Code palette", ["classic", "mint", "halloween"], workspace.Setting("native.codePalette") is { Length: > 0 } palette ? palette : "classic", value => workspace.SetSetting("native.codePalette", value)));
        void SaveTypography() {
            workspace.SetSetting("native.textFont", textFont.SelectedItem?.ToString() ?? "Rubik");
            workspace.SetSetting("native.codeFont", codeFont.SelectedItem?.ToString() ?? "JetBrains Mono");
            SaveSize(textSize, "native.textSize", 11, 28);
            SaveSize(codeSize, "native.codeSize", 10, 26);
            KronoDesign.ApplyTypography(workspace, this);
        }
        textFont.SelectionChanged += (_, _) => SaveTypography();
        codeFont.SelectionChanged += (_, _) => SaveTypography();
        void SaveSize(KronoTextBox field, string key, double minimum, double maximum)
        {
            if (double.TryParse(field.Text, System.Globalization.NumberStyles.AllowDecimalPoint, culture, out var value) &&
                double.IsFinite(value) && value >= minimum && value <= maximum)
                workspace.SetSetting(key, value.ToString(culture));
        }
        void AttachSize(KronoTextBox field, string key, double minimum, double maximum)
        {
            void Commit()
            {
                if (double.TryParse(field.Text, System.Globalization.NumberStyles.AllowDecimalPoint, culture, out var value) &&
                    double.IsFinite(value)) workspace.SetSetting(key, Math.Clamp(value, minimum, maximum).ToString(culture));
                field.Text = workspace.Setting(key) is { Length: > 0 } saved ? saved : "14";
                SaveTypography();
            }
            field.LostFocus += (_, _) => Commit();
            field.PreviewKeyDown += (_, args) => {
                if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; Commit(); }
            };
        }
        AttachSize(textSize, "native.textSize", 11, 28);
        AttachSize(codeSize, "native.codeSize", 10, 26);
        var liveMarkdown = KronoDesign.Toggle("Live Markdown while editing", workspace.Setting(NativeMarkdown.LiveEditorSetting) == "true");
        AutomationProperties.SetAutomationId(liveMarkdown, "LiveMarkdownEditing");
        liveMarkdown.Toggled += (_, _) => {
            workspace.SetSetting(NativeMarkdown.LiveEditorSetting, liveMarkdown.IsOn ? "true" : "false");
            NativeMarkdown.RefreshEditors(workspace);
        };
        panel.Children.Add(liveMarkdown);
        AddCategory("Plugins");
        AddUpdateSettings(AddCategory("Updates"));
        var names = pages.Keys.ToList();
        names.Insert(names.IndexOf("Plugins"), "Instructions");
        var categories = new ListView {
            // The sidebar already animates its entrance horizontally.
            ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection(),
            ItemsSource = names,
            SelectionMode = ListViewSelectionMode.Single
        };
        AutomationProperties.SetAutomationId(categories, "SettingsCategories");
        var sidebar = new StackPanel { Spacing = 4 };
        sidebar.Children.Add(Heading("Settings"));
        sidebar.Children.Add(categories);
        ShowContext(sidebar);
        var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var modelScroll = new ScrollViewer { Content = modelPanel, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        modelScroll.SizeChanged += (_, args) => modelPanel.Width = Math.Min(modelPanel.MaxWidth, args.NewSize.Width);
        var views = pages.ToDictionary(entry => entry.Key,
            entry => KronoDesign.ScrollPage(entry.Key == "Models" ? modelScroll : entry.Value));
        views.Add("Instructions", BuildInstructionsSettings(page));
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
