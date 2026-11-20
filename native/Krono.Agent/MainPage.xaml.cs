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
    readonly Button modelChoice = new() { Content = "Choose model" };
    Button? sideChatButton;
    readonly Button send = new() { Content = "Send" };
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
        InitializeComponent();
        BrowserNavigation.Icon = KronoDesign.NavigationIcon(Symbol.World);
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
            catch (Exception error) { SetAuthError(error.Message); }
            finally { StartupProgress.IsActive = false; StartupProgress.Visibility = Visibility.Collapsed; if (Nav.Visibility != Visibility.Visible) SignIn.Visibility = Visibility.Visible; }
        };
        Unloaded += (_, _) => { confirmationWait?.Cancel(); sessionGeneration++; sessionTimer.Stop(); StopSync(); StopSchedules(); sideChat?.Dispose(); worker?.Dispose(); browser?.Dispose(); terminal?.Dispose(); games?.Dispose(); workspace.Dispose(); };
    }
    async void Login(object sender, RoutedEventArgs e) => await Authenticate(false);
    async void SignInWithGoogle(object sender, RoutedEventArgs e)
    {
        if (authenticating) return;
        authenticating = true;
        SetAuthError("");
        try {
            if (!await AuthorizeGoogle(false)) return;
            PasswordInput.Password = "";
            PasswordVisibility.IsChecked = false;
            Enter();
        }
        catch (Exception error) { SetAuthError(error.Message); }
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
        if (e.Key != Windows.System.VirtualKey.Enter || PasswordInput.FocusState == FocusState.Unfocused) return;
        e.Handled = true;
        await Authenticate(CreateAccountButton.Visibility == Visibility.Visible);
    }
    void SetRegistrationMode(bool register)
    {
        AuthTitle.Text = register ? "Create your account" : "Welcome to Krono";
        AuthSubtitle.Text = register ? "Sign up to start your Krono workspace." : "Sign in to your workspace.";
        LoginButton.Visibility = NewAccountPrompt.Visibility = register ? Visibility.Collapsed : Visibility.Visible;
        CreateAccountButton.Visibility = ExistingAccountPrompt.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
        SetAuthError("");
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
    void SetAuthError(string message)
    {
        AuthError.Text = message;
        AuthError.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        SetAuthError("");
        var email = EmailInput.Text.Trim();
        var password = PasswordInput.Password;
        try {
            if (!await workspace.Login(email, password, register)) { ShowConfirmation(email, true); _ = WaitForConfirmation(email, password); return; }
            PasswordInput.Password = ""; PasswordVisibility.IsChecked = false; Enter();
        }
        catch (Workspace.EmailConfirmationRequiredException) { ShowConfirmation(email, false); }
        catch (Exception error) { SetAuthError(error.Message); }
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
            if (item.Tag?.ToString() != "chats" && sideChat is not null) SetSideChatVisible(false);
            switch (item.Tag?.ToString()) {
                case "chats": ShowChats(); break;
                case "projects": ShowProjects(); break;
                case "settings": ShowSettings(); break;
                case "browser": browser ??= new BrowserWorkspace(workspace, this); KronoDesign.Present(Body, browser); break;
                case "terminal": terminal ??= new TerminalWorkspace(worker!); KronoDesign.Present(Body, terminal); break;
                case "games": games ??= new GamesWorkspace(workspace); KronoDesign.Present(Body, games); break;
                case "schedule": ShowSchedules(); break;
            }
            UpdateActivityVisibility();
        } catch (Exception error) { Error(error); }
    }
    public void Error(Exception error) { Notice.Severity = InfoBarSeverity.Error; Notice.Message = error.Message; Notice.IsOpen = true; }
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
            messages.Children.Clear(); composer.Text = fullResponse = progress.Text = ""; streaming = null; model = ""; modelChoice.Content = "Choose model";
            ClearAttachments();
            mode = "chat"; permissions = "ask"; if (modeSelector is not null) modeSelector.SelectedItem = mode; if (permissionSelector is not null) permissionSelector.SelectedItem = permissions;
            activityChatId = null; RunActivity.IsOpen = false; Approval.IsOpen = Notice.IsOpen = false; Nav.Visibility = Visibility.Collapsed; SignIn.Visibility = Visibility.Visible;
        }
    }
    public async Task<bool> Confirm(string title, string message, string action = "Allow")
    {
        var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        KronoDesign.ApplyControlAccents(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    static TextBlock Heading(string text) => new() { Text = text, FontFamily = (FontFamily)Application.Current.Resources["KronoDisplayFont"], FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
    Button Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => { try { action(); } catch (Exception error) { Error(error); } }; return button; }
    Button Button(string label, Func<Task> action) { var button = new Button { Content = label }; button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Error(error); } }; return button; }
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
            title.FontSize = 23;
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
            var projectsButton = HistoryAction("Projects & memory", KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Folder), ShowProjects);
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
            var fadedTranscript = KronoDesign.ScrollPage(transcript);
            Grid.SetRow(fadedTranscript, 1); conversation.Children.Add(fadedTranscript);
            Grid.SetRow(progress, 2); conversation.Children.Add(progress);
            var input = new StackPanel { Spacing = 4 };
            input.Children.Add(attachmentTray);
            input.Children.Add(composer);
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
        if (backToChats is not null) backToChats.Visibility = Visibility.Collapsed;
        ShowContext(chatHistory);
        RefreshChatModelLabel();
        KronoDesign.Present(Body, chatView); ReloadChats(); if (runId is null) LoadChat();
        SetSideChatVisible(sideChat is not null && sideChat.ParentChatId == chatId);
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
            var config = project is null ? null : workspace.ProjectPreferences(project);
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
            var messageId = workspace.AddMessage(chatId, "user", text);
            composer.Text = ""; ClearAttachments();
            if (workspace.Query("SELECT id FROM messages WHERE chat_id=$p0", chatId).Count == 1) workspace.Execute("UPDATE chats SET name=$p0 WHERE id=$p1", title[..Math.Min(42, title.Length)], chatId);
            LoadChat(); ReloadChats(); streaming = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["KronoTextFont"], FontSize = (double)Application.Current.Resources["KronoTextSize"], LineHeight = 24 }; messages.Children.Add(streaming); fullResponse = "";
            var project = workspace.Query("SELECT project_id FROM chats WHERE id=$p0", chatId).First()["project_id"]?.ToString();
            var config = project is null ? null : workspace.ProjectPreferences(project);
            var dirs = project is null ? [] : workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", project).Select(p => p["path"]!.ToString()).ToArray();
            var memories = workspace.Query("SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id=$p0)", project).Select(p => p["content"]!.ToString()).ToArray();
            runId = Guid.NewGuid().ToString(); RefreshComposerAction(); chatList.IsEnabled = false; progress.Text = "Working…";
            activityChatId = chatId; RunActivity.Title = "Agent running"; RunActivity.Message = workspace.Query("SELECT name FROM chats WHERE id=$p0", chatId).First()["name"]?.ToString(); RunActivity.IsClosable = false; UpdateActivityVisibility();
            var history = workspace.Query("SELECT role,content FROM messages WHERE chat_id=$p0 AND deleted_at IS NULL ORDER BY id=$p1,created_at,rowid", chatId, messageId).Select(p => new { role = p["role"]!.ToString(), content = p["content"]!.ToString() }).ToArray();
            if (workspace.Setting("native.chatContext") == "Recent messages") history = history.TakeLast(20).ToArray();
            var reasoning = workspace.Setting(ChatReasoningKey);
            var request = JsonSerializer.SerializeToNode(new { providerId = provider, apiKey, endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled, modelId = model, mode, permissions, messages = history, projectDirs = dirs, instructions = config?["instructions"]?.ToString() ?? "", memories })!.AsObject();
            if (image is not null) request["messages"]!.AsArray()[^1]!["imagePng"] = image;
            if (reasoning is "low" or "medium" or "high") request["reasoningEffort"] = reasoning;
            await worker!.Request(runId, "run", request);
            if (generation != sessionGeneration) return;
            workspace.AddMessage(chatId, "assistant", fullResponse); progress.Text = "Done";
            RunActivity.Title = "Response ready"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility();
        } catch (Exception error) { if (generation == sessionGeneration) { Error(error); progress.Text = "Stopped"; RunActivity.Title = "Agent stopped"; RunActivity.IsClosable = true; if (ReferenceEquals(Body.Content, chatView)) activityChatId = null; UpdateActivityVisibility(); } }
        finally { if (generation == sessionGeneration) { runId = null; RefreshComposerAction(); chatList.IsEnabled = true; Approval.IsOpen = false; approvalId = null; ClearApprovalDetails(); if (workspace.AccountId.Length > 0) { workspace.FlushSideTransfers(); LoadChat(); } } }
    }
    void WorkerEvent(string id, string type, JsonNode? data) {
        terminal?.Receive(id, type, data);
        if (id != runId) return;
        if (type == "approval" && data is not null) DisplayApproval(data);
        if (type == "chunk" && data?["type"]?.ToString() == "text") { fullResponse += data["content"]?.ToString(); if (streaming is not null) streaming.Text = fullResponse; transcript.ChangeView(null, transcript.ScrollableHeight, null); }
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
    void ApplyTheme(string value, bool animate = false)
    {
        KronoDesign.ApplyPalette(value == "halloween");
        RequestedTheme = value == "light" ? ElementTheme.Light : value is "dark" or "halloween" ? ElementTheme.Dark : ElementTheme.Default;
        if (MainWindow.Instance?.Content is FrameworkElement root) root.RequestedTheme = RequestedTheme;
        HalloweenPumpkin.Visibility = HalloweenCorner.Visibility = value == "halloween" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var backdrop in WorkspacePattern.Children.OfType<GameBackdrop>().Concat(LoginPattern.Children.OfType<GameBackdrop>()))
            backdrop.SetHalloween(value == "halloween");
        MainWindow.Instance?.ShowHalloween(value == "halloween");
        KronoInputMotion.RefreshPalette(this);
        if (animate) KronoDesign.AnimateThemeChange(this);
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
        panel = AddCategory("Browser");
        var homePage = new KronoTextBox { Header = "Browser new tab URL", Text = workspace.Setting("browser.homePage") is { Length: > 0 } home ? home : "https://www.google.com/" };
        AutomationProperties.SetAutomationId(homePage, "BrowserHomePage");
        void SaveBrowserHome()
        {
            var value = homePage.Text.Trim();
            if (value.Length > 0 && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))) {
                Error(new InvalidOperationException("Enter a full HTTP or HTTPS address."));
                return;
            }
            workspace.SetSetting("browser.homePage", value);
            homePage.Text = value.Length > 0 ? value : "https://www.google.com/";
        }
        homePage.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; SaveBrowserHome(); } };
        panel.Children.Add(homePage);
        panel.Children.Add(Button("Save browser home page", SaveBrowserHome));
        panel = AddCategory("Models");
        var secret = new PasswordBox { Header = "Provider API key · optional for local servers", HorizontalAlignment = HorizontalAlignment.Stretch };
        KronoInputMotion.Attach(secret, "KronoPasswordInput");
        var endpoint = new KronoTextBox { Header = "Server base URL · include /v1", Text = ProviderEndpoint };
        AutomationProperties.SetAutomationId(endpoint, "ProviderEndpoint");
        var tools = new KronoCheckBox { Content = "This model supports tool calling", IsChecked = ProviderToolsEnabled };
        AutomationProperties.SetAutomationId(tools, "ProviderTools");
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7 }; AutomationProperties.SetAutomationId(connectionStatus, "ProviderConnectionStatus");
        var models = new KronoComboBox { Header = "Model", HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, Text = model };
        var profileHelp = new TextBlock { Text = ProviderCatalog.Help(provider), TextWrapping = TextWrapping.Wrap, Opacity = .65 };
        void SaveEndpoint() { try {
            var url = ProviderCatalog.ParseEndpoint(endpoint.Text);
            workspace.SetSetting("native.endpoint." + provider, url.AbsoluteUri.TrimEnd('/')); connectionStatus.Text = "Endpoint saved.";
        } catch (Exception error) { Error(error); } }
        var saveEndpoint = Button("Save endpoint", SaveEndpoint);
        endpoint.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; SaveEndpoint(); } };
        var modelSettings = new StackPanel { Spacing = 8 };
        var modelVisibility = new StackPanel { Spacing = 2 };
        var modelStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        int modelRequest = 0;
        void RefreshModelVisibility()
        {
            modelRequest++;
            modelStatus.Text = "";
            modelSettings.Visibility = workspace.Key(provider).Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            modelVisibility.Children.Clear();
        }
        void EndpointVisibility()
        {
            endpoint.Visibility = saveEndpoint.Visibility = CompatibleEndpoint ? Visibility.Visible : Visibility.Collapsed;
            tools.Visibility = CompatibleEndpoint ? Visibility.Visible : Visibility.Collapsed;
            tools.IsEnabled = model.Length > 0;
            RefreshModelVisibility();
        }
        ToolTipService.SetToolTip(tools, "Choose Use model first. Tool support is saved separately for each provider and model.");
        var providerIds = ProviderCatalog.Ids; var providerNames = providerIds.Select(ProviderCatalog.Name).ToArray();
        panel.Children.Add(Choices("Provider", providerNames, ProviderCatalog.Name(provider), value => {
            provider = providerIds[Array.IndexOf(providerNames, value)]; workspace.SetSetting("native.provider", provider); secret.Password = ""; model = workspace.Setting("native.model." + provider); models.ItemsSource = null; models.Text = model; endpoint.Text = ProviderEndpoint; tools.IsChecked = ProviderToolsEnabled; connectionStatus.Text = ""; profileHelp.Text = ProviderCatalog.Help(provider); EndpointVisibility(); _ = LoadProviderModels();
        }));
        tools.Checked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "true"); }; tools.Unchecked += (_, _) => { if (CompatibleEndpoint && model.Length > 0) workspace.SetSetting(ModelToolsSetting, "false"); };
        EndpointVisibility(); panel.Children.Add(endpoint); panel.Children.Add(saveEndpoint); panel.Children.Add(secret);
        AutomationProperties.SetAutomationId(secret, "ProviderKey");
        void SaveKey()
        {
            if (string.IsNullOrWhiteSpace(secret.Password)) return;
            try { workspace.SaveKey(provider, secret.Password.Trim()); secret.Password = ""; RefreshModelVisibility(); _ = LoadProviderModels(); }
            catch (Exception error) { Error(error); }
        }
        secret.PreviewKeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; SaveKey(); } };
        panel.Children.Add(Button("Save key", SaveKey));
        panel.Children.Add(Button("Test connection", async () => { try { var result = await worker!.Request(Guid.NewGuid().ToString(), "test", new { providerId = provider, apiKey = workspace.Key(provider), endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled }); connectionStatus.Text = result?["success"]?.GetValue<bool>() == true ? "Connected · " + result["latencyMs"] + " ms" : "Connection failed. Check the server, endpoint and credentials."; } catch (Exception error) { Error(error); } }));
        panel.Children.Add(connectionStatus);
        panel.Children.Add(modelSettings);
        modelSettings.Children.Add(models);
        AutomationProperties.SetAutomationId(models, "ModelSelector");
        modelSettings.Children.Add(Button("Load available models", LoadProviderModels));
        modelSettings.Children.Add(modelStatus);
        modelSettings.Children.Add(modelVisibility);
        async Task LoadProviderModels()
        {
            var selectedProvider = provider;
            var key = workspace.Key(selectedProvider);
            if (key.Length == 0 || worker is null) return;
            var request = ++modelRequest;
            var generation = sessionGeneration;
            modelStatus.Text = "Loading models…";
            try {
                var result = await worker.Request(Guid.NewGuid().ToString(), "models",
                    new { providerId = selectedProvider, apiKey = key, endpoint = ProviderEndpoint, toolsEnabled = ProviderToolsEnabled });
                if (request != modelRequest || generation != sessionGeneration || provider != selectedProvider) return;
                var ids = result!.AsArray().Select(entry => entry!["id"]!.ToString()).Distinct().Order().ToArray();
                var hidden = HiddenModels(selectedProvider);
                var current = models.Text;
                models.ItemsSource = ids;
                models.Text = current;
                modelVisibility.Children.Clear();
                modelStatus.Text = ids.Length == 0 ? "This provider returned no models." : "Show in the chat model picker";
                foreach (var id in ids) {
                    var checkbox = new KronoCheckBox { Content = id, IsChecked = !hidden.Contains(id) };
                    void SaveVisibility()
                    {
                        if (provider != selectedProvider || generation != sessionGeneration) return;
                        var selection = HiddenModels(selectedProvider);
                        if (checkbox.IsChecked == true) selection.Remove(id);
                        else selection.Add(id);
                        workspace.SetSetting("native.hiddenModels." + selectedProvider, JsonSerializer.Serialize(selection));
                    }
                    checkbox.Checked += (_, _) => SaveVisibility();
                    checkbox.Unchecked += (_, _) => SaveVisibility();
                    modelVisibility.Children.Add(checkbox);
                }
            } catch (Exception) {
                if (request == modelRequest && generation == sessionGeneration && provider == selectedProvider)
                    modelStatus.Text = "Could not load this provider’s models.";
            }
        }
        void UseModel()
        {
            model = models.Text.Trim();
            workspace.SetSetting("native.model." + provider, model);
            tools.IsChecked = ProviderToolsEnabled;
            tools.IsEnabled = model.Length > 0;
        }
        models.PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Enter || models.IsDropDownOpen) return;
            args.Handled = true;
            UseModel();
        };
        modelSettings.Children.Add(Button("Use model", UseModel));
        modelSettings.Children.Add(tools);
        _ = LoadProviderModels();
        panel.Children.Add(profileHelp);
        panel.Children.Add(new TextBlock { Text = "API keys are encrypted for this Windows account. Provider usage and billing follow your provider account.", TextWrapping = TextWrapping.Wrap });
        panel = AddCategory("Appearance");
        panel.Children.Add(Choices("Appearance", ["system", "dark", "light", "halloween"], workspace.Setting("native.theme") is { Length: > 0 } t ? t : "system", value => { workspace.SetSetting("native.theme", value); ApplyTheme(value, animate: true); }));
        var textFont = new KronoTextBox { Header = "Text font", Text = workspace.Setting("native.textFont") is { Length: > 0 } tf ? tf : "Rubik" };
        var codeFont = new KronoTextBox { Header = "Code font", Text = workspace.Setting("native.codeFont") is { Length: > 0 } cf ? cf : "JetBrains Mono" };
        AutomationProperties.SetAutomationId(textFont, "TextFontFamily"); AutomationProperties.SetAutomationId(codeFont, "CodeFontFamily");
        var textSize = new NumberBox { Header = "Text size", Minimum = 11, Maximum = 28, Value = KronoDesign.TextSize };
        var codeSize = new NumberBox { Header = "Code size", Minimum = 10, Maximum = 26, Value = KronoDesign.CodeSize };
        AutomationProperties.SetAutomationId(textSize, "TextFontSize"); AutomationProperties.SetAutomationId(codeSize, "CodeFontSize");
        AddFontRow(textFont, textSize);
        AddFontRow(codeFont, codeSize);
        void AddFontRow(KronoTextBox family, NumberBox size)
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
            workspace.SetSetting("native.textFont", textFont.Text.Trim()); workspace.SetSetting("native.codeFont", codeFont.Text.Trim());
            workspace.SetSetting("native.textSize", (double.IsFinite(textSize.Value) ? Math.Clamp(textSize.Value, 11, 28) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            workspace.SetSetting("native.codeSize", (double.IsFinite(codeSize.Value) ? Math.Clamp(codeSize.Value, 10, 26) : 14).ToString(System.Globalization.CultureInfo.InvariantCulture));
            KronoDesign.ApplyTypography(workspace, this);
        }
        codeFont.PreviewKeyDown += (_, args) => {
            if (args.Key != Windows.System.VirtualKey.Enter) return;
            args.Handled = true;
            SaveTypography();
        };
        panel.Children.Add(Button("Save typography", SaveTypography));
        panel.Children.Add(new TextBlock { Text = "Bundled fonts: Rubik, Space Grotesk and JetBrains Mono. You can also enter an installed font name. Text and code sizes are independent.", TextWrapping = TextWrapping.Wrap, Opacity = .65 });
        AddUpdateSettings(AddCategory("Updates"));
        var categories = new ListView { ItemsSource = pages.Keys.ToArray(), SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetAutomationId(categories, "SettingsCategories");
        var sidebar = new StackPanel { Spacing = 4 };
        sidebar.Children.Add(Heading("Settings"));
        sidebar.Children.Add(categories);
        ShowContext(sidebar);
        var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var views = pages.ToDictionary(entry => entry.Key, entry => KronoDesign.ScrollPage(entry.Value));
        categories.SelectionChanged += (_, _) => {
            if (categories.SelectedItem is not string selected) return;
            KronoDesign.Present(page, views[selected]);
        };
        categories.SelectedIndex = 0;
        KronoDesign.Present(Body, page);
    }
    void ShowProjects()
    {
        if (backToChats is not null) backToChats.Visibility = Visibility.Visible;
        SetSideChatVisible(false);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Heading("Project"));
        var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name"); string? selected = null;
        var picker = new KronoComboBox { Header = "Project", ItemsSource = projects.Select(p => p["name"]!.ToString()).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new KronoTextBox { Header = "Name" }; var folders = new KronoTextBox { Header = "Project folders · one absolute path per line", AcceptsReturn = true, MinHeight = 70 };
        AutomationProperties.SetAutomationId(name, "ProjectName"); AutomationProperties.SetAutomationId(folders, "ProjectFolders");
        var instructions = new KronoTextBox { Header = "Project instructions", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
        var memory = new KronoTextBox { Header = "Project memory", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        AutomationProperties.SetAutomationId(instructions, "ProjectInstructions"); AutomationProperties.SetAutomationId(memory, "ProjectMemory");
        var global = new KronoTextBox { Header = "Global memory", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Text = workspace.Query("SELECT content FROM memories WHERE id='global'").FirstOrDefault()?["content"]?.ToString() ?? "" };
        AutomationProperties.SetAutomationId(global, "GlobalMemory");
        string projectPermissions = "ask";
        var permissionBox = Choices("Default permissions", ["ask", "project", "full"], "ask", value => projectPermissions = value);
        permissionBox.SelectionChanged += (_, _) => MarkFullAccess(permissionBox, projectPermissions);
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex < 0) return; selected = projects[picker.SelectedIndex]["id"]!.ToString(); name.Text = projects[picker.SelectedIndex]["name"]!.ToString(); folders.Text = string.Join(Environment.NewLine, workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0", selected).Select(p => p["path"]!.ToString())); var config = workspace.ProjectPreferences(selected); instructions.Text = config?["instructions"]?.ToString() ?? ""; permissionBox.SelectedItem = config?["permissions"]?.ToString() ?? "ask"; memory.Text = workspace.Query("SELECT content FROM memories WHERE id=$p0", "project:" + selected).FirstOrDefault()?["content"]?.ToString() ?? ""; };
        panel.Children.Add(picker); panel.Children.Add(Button("New project", () => { selected = null; picker.SelectedIndex = -1; name.Text = folders.Text = instructions.Text = memory.Text = ""; }));
        panel.Children.Add(name); panel.Children.Add(folders); panel.Children.Add(instructions); panel.Children.Add(permissionBox); panel.Children.Add(memory);
        panel.Children.Add(Button("Add folder…", async () => {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker(); folderPicker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance!));
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is not null) folders.Text += (folders.Text.Length == 0 ? "" : Environment.NewLine) + folder.Path;
        }));
        panel.Children.Add(Button("Save project", async () => { try {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Give the project a name.");
            var directories = folders.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFullPath).Distinct().ToArray(); if (directories.Any(p => !Directory.Exists(p))) throw new InvalidOperationException("Every project folder must exist.");
            if (projectPermissions == "full" && (selected is null || workspace.ProjectPreferences(selected)?["permissions"]?.ToString() != "full")) {
                var generation = sessionGeneration;
                if (!await Confirm("Enable full access for this project?", "Tools in this project can run commands and change files with your Windows permissions without asking.", "Enable full access") || generation != sessionGeneration) return;
            }
            var id = selected ?? Guid.NewGuid().ToString(); var now = Workspace.Now;
            workspace.Execute("BEGIN"); try {
            workspace.Execute("INSERT INTO projects(id,account_id,name,created_at,updated_at) VALUES($p0,$p1,$p2,$p3,$p3) ON CONFLICT(id) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at", id, workspace.AccountId, name.Text.Trim(), now);
            workspace.Execute("DELETE FROM project_directories WHERE project_id=$p0", id); foreach (var directory in directories) workspace.Execute("INSERT INTO project_directories(id,project_id,device_id,path) VALUES($p0,$p1,'local',$p2)", Guid.NewGuid().ToString(), id, directory);
            workspace.SetSetting("project." + id, new JsonObject { ["instructions"] = instructions.Text, ["permissions"] = projectPermissions }.ToJsonString()); SaveMemory("project:" + id, id, memory.Text); workspace.Execute("COMMIT"); } catch { workspace.Execute("ROLLBACK"); throw; } ShowProjects();
        } catch (Exception error) { Error(error); } }));
        var memoryPanel = new StackPanel { Spacing = 12 };
        memoryPanel.Children.Add(Heading("Shared memory"));
        memoryPanel.Children.Add(new TextBlock { Text = "This memory applies across projects.", TextWrapping = TextWrapping.Wrap });
        memoryPanel.Children.Add(global);
        memoryPanel.Children.Add(Button("Save global memory", () => SaveMemory("global", null, global.Text)));
        Border Card(StackPanel content)
        {
            var card = new Border { Child = content, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), VerticalAlignment = VerticalAlignment.Top };
            void SetPalette() => card.Background = KronoDesign.ThemeBrush("KronoSurface", card.ActualTheme);
            card.Loaded += (_, _) => SetPalette();
            card.ActualThemeChanged += (_, _) => SetPalette();
            return card;
        }
        var columns = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var projectCard = Card(panel);
        var memoryCard = Card(memoryPanel);
        columns.Children.Add(projectCard);
        columns.Children.Add(memoryCard);
        Grid.SetRow(memoryCard, 1);
        columns.SizeChanged += (_, args) => {
            var wide = args.NewSize.Width >= 760;
            columns.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(memoryCard, wide ? 0 : 1);
            Grid.SetColumn(memoryCard, wide ? 1 : 0);
        };
        var page = new StackPanel { Spacing = 12, MaxWidth = 1020, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(Heading("Projects & memory"));
        page.Children.Add(columns);
        var scroller = KronoDesign.ScrollPage(page);
        scroller.SizeChanged += (_, args) => page.Width = Math.Min(page.MaxWidth, Math.Max(0, args.NewSize.Width - 16));
        KronoDesign.Present(Body, scroller);
    }
    void SaveMemory(string id, string? project, string content) => workspace.Execute("INSERT INTO memories(id,account_id,project_id,content,created_at,updated_at) VALUES($p0,$p1,$p2,$p3,$p4,$p4) ON CONFLICT(id) DO UPDATE SET content=excluded.content,updated_at=excluded.updated_at", id, workspace.AccountId, project, content, Workspace.Now);
}
