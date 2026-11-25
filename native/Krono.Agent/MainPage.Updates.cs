using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krono_Agent;

public sealed partial class MainPage
{
    const string ReleaseRoot = "https://github.com/KronoAgent/Krono_Agent/releases";
    const string InstallerName = "Krono-Agent-Setup-x64.exe";
    static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(4) };
    readonly CancellationTokenSource updateLifetime = new();
    readonly record struct AvailableUpdate(Version Version, Uri Url, long Size, string Hash);
    AvailableUpdate? availableUpdate;
    string? downloadedInstaller;
    Button? settingsUpdateButton;
    ProgressBar? settingsUpdateProgress;
    int? updateDownloadPercent;
    bool installingUpdate;
    (Symbol Icon, string Title, string Detail)? updateCheckResult;
    Border? updateStatusCard;
    ContentControl? updateStatusIcon;
    TextBlock? updateStatusTitle, updateStatusDetail;

    void ShowUpdateCheck(Symbol icon, string title, string detail)
    {
        updateCheckResult = (icon, title, detail);
        RefreshUpdateStatus();
    }

    void RefreshUpdateStatus()
    {
        if (updateStatusCard is null) return;
        if (updateCheckResult is not { } result) {
            updateStatusCard.Visibility = Visibility.Collapsed;
            return;
        }
        var icon = result.Icon == Symbol.Download
            ? (IconElement)KronoDesign.NavigationIcon(Symbol.Download) : new SymbolIcon(result.Icon);
        icon.Foreground = updateStatusIcon!.Foreground;
        updateStatusIcon.Content = icon;
        updateStatusTitle!.Text = result.Title;
        updateStatusDetail!.Text = result.Detail;
        updateStatusCard.Visibility = Visibility.Visible;
        if (FrameworkElementAutomationPeer.CreatePeerForElement(updateStatusTitle) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    void AddUpdateStatus(StackPanel panel)
    {
        var icon = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        updateStatusIcon = icon;
        var iconTile = new Border { Child = icon, Width = 36, Height = 36,
            CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        updateStatusTitle = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(updateStatusTitle, AutomationLiveSetting.Polite);
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap };
        updateStatusDetail = detail;
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(updateStatusTitle);
        text.Children.Add(detail);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(iconTile);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(12),
            BorderThickness = new Thickness(1), MinWidth = 240, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(card, "UpdateCheckStatus");
        void ApplyPalette()
        {
            Brush Role(string name) => KronoDesign.ThemeBrush(name, card.ActualTheme);
            card.Background = Role("KronoSurface");
            card.BorderBrush = Role("KronoBorder");
            iconTile.Background = Role("KronoSidebar");
            icon.Foreground = Role("KronoAccentForeground");
            if (icon.Content is IconElement glyph) glyph.Foreground = icon.Foreground;
            detail.Foreground = Role("KronoMuted");
        }
        card.Loaded += (_, _) => ApplyPalette();
        card.ActualThemeChanged += (_, _) => ApplyPalette();
        KronoInputMotion.RegisterPaletteRefresh(card, ApplyPalette);
        updateStatusCard = card;
        panel.Children.Add(card);
        RefreshUpdateStatus();
    }

    bool checkingUpdate, showUpdateCheckResult, updateInProgress;
    static Version InstalledVersion => typeof(MainPage).Assembly.GetName().Version ?? new Version(0, 0);
    bool HasRunningWork => runId is not null || scheduledRun is not null || sideChat?.IsRunning == true;
    internal bool HasBackgroundWork => HasRunningWork || updateInProgress;

    void InitializeUpdates()
    {
        Loaded += async (_, _) => { updateTimer.Start(); await CheckUpdates(); };
        updateTimer.Tick += async (_, _) => await CheckUpdates();
        Unloaded += (_, _) => { updateTimer.Stop(); updateLifetime.Cancel(); DeleteDownload(downloadedInstaller); };
    }

    void AddUpdateSettings(StackPanel panel)
    {
        var version = InstalledVersion.ToString(3);
        panel.Children.Add(new TextBlock { Text = "Krono Agent " + version });
        panel.Children.Add(Button("Check for updates", async () => await CheckUpdates(true)));
        settingsUpdateButton = new Button { Style = (Style)Application.Current.Resources["KronoPrimaryButton"] };
        settingsUpdateButton.Click += (_, _) => UpdateAction();
        panel.Children.Add(settingsUpdateButton);
        settingsUpdateProgress = new ProgressBar { Maximum = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(settingsUpdateProgress, "Update download progress");
        panel.Children.Add(settingsUpdateProgress);
        AddUpdateStatus(panel);
        RefreshUpdateButton();
    }

    async Task CheckUpdates(bool manual = false)
    {
        if (updateInProgress || downloadedInstaller is not null) {
            if (manual && availableUpdate is { } update) ShowAvailableUpdate(update.Version);
            return;
        }
        showUpdateCheckResult |= manual;
        if (checkingUpdate) return;
        checkingUpdate = true;
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/KronoAgent/Krono_Agent/releases/latest");
            request.Headers.UserAgent.ParseAdd("KronoAgent/" + InstalledVersion.ToString(3));
            using var response = await UpdateHttp.SendAsync(request, updateLifetime.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(updateLifetime.Token));
            var release = json.RootElement;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var current = new Version(InstalledVersion.ToString(3));
            if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version))
                throw new InvalidDataException("The published release has an invalid version.");
            if (version <= current) {
                availableUpdate = null;
                if (showUpdateCheckResult) {
                    ShowUpdateCheck(Symbol.Accept, "You’re up to date", $"Krono Agent v{current.ToString(3)} is the latest version.");
                    ShowNotice("Krono Agent is up to date.");
                }
                else if (updateCheckResult?.Icon == Symbol.Download) {
                    updateCheckResult = null;
                    RefreshUpdateStatus();
                }
                return;
            }
            var url = new Uri($"{ReleaseRoot}/download/v{version}/{InstallerName}");
            foreach (var asset in release.GetProperty("assets").EnumerateArray()) {
                if (asset.GetProperty("name").GetString() != InstallerName) continue;
                var digest = asset.GetProperty("digest").GetString() ?? "";
                var size = asset.GetProperty("size").GetInt64();
                if (size is <= 0 or > 512L * 1024 * 1024 || digest.Length != 71 ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                    asset.GetProperty("browser_download_url").GetString() != url.AbsoluteUri) break;
                availableUpdate = new(version, url, size, Convert.ToHexString(Convert.FromHexString(digest[7..])));
                ShowAvailableUpdate(version);
                if (showUpdateCheckResult) ShowNotice($"Krono Agent v{version.ToString(3)} is available.");
                return;
            }
            throw new InvalidDataException("The release has no verified Windows installer.");
        } catch (Exception error) {
            if (showUpdateCheckResult && !updateLifetime.IsCancellationRequested) {
                ShowUpdateCheck(Symbol.Important, "Couldn’t check for updates", FriendlyError(error));
                ShowNotice("Couldn’t check for updates. " + FriendlyError(error), success: false);
            }
        } finally {
            checkingUpdate = showUpdateCheckResult = false;
            RefreshUpdateButton();
        }
    }

    void ShowAvailableUpdate(Version version) =>
        ShowUpdateCheck(Symbol.Download, $"Version {version.ToString(3)} is available",
            $"You’re running v{InstalledVersion.ToString(3)}.");

    void RefreshUpdateButton()
    {
        UpdateButton.Visibility = availableUpdate.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.IsEnabled = availableUpdate.HasValue && !updateInProgress;
        var label = installingUpdate ? "Starting installer…" : updateDownloadPercent.HasValue ? "Downloading update…" : "Download and install update";
        UpdateLabel.Text = label;
        AutomationProperties.SetName(UpdateButton, label);
        ToolTipService.SetToolTip(UpdateButton, label);
        if (settingsUpdateButton is not null) {
            settingsUpdateButton.Content = label;
            settingsUpdateButton.IsEnabled = availableUpdate.HasValue && !updateInProgress;
        }
        if (settingsUpdateProgress is { } progress) {
            progress.Visibility = updateDownloadPercent.HasValue ? Visibility.Visible : Visibility.Collapsed;
            progress.Value = updateDownloadPercent ?? 0;
        }
    }

    async void UpdateAction()
    {
        if (updateInProgress || availableUpdate is not { } update) return;
        updateInProgress = true;
        RefreshUpdateButton();
        try {
            downloadedInstaller ??= await DownloadInstaller(update);
            updateDownloadPercent = null;
            RefreshUpdateButton();
            updateLifetime.Token.ThrowIfCancellationRequested();
            var window = MainWindow.Instance ?? throw new InvalidOperationException("Krono Agent is closing.");
            installingUpdate = true;
            RefreshUpdateButton();
            var setupInfo = new ProcessStartInfo(downloadedInstaller) { UseShellExecute = false };
            foreach (var argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART",
                "/NORESTARTAPPLICATIONS", "/CLOSEAPPLICATIONS", "/KRONOUPDATE=1", $"/KRONOPID={Environment.ProcessId}" })
                setupInfo.ArgumentList.Add(argument);
            using var setup = Process.Start(setupInfo);
            if (setup is null) throw new InvalidOperationException("The update installer could not be started.");
            downloadedInstaller = null; // The setup process still needs the file.
            window.CloseApproved();
        } catch (Exception error) {
            if (!updateLifetime.IsCancellationRequested) Error(error);
        } finally {
            updateInProgress = installingUpdate = false;
            updateDownloadPercent = null;
            RefreshUpdateButton();
        }
    }

    async Task<string> DownloadInstaller(AvailableUpdate update)
    {
        updateDownloadPercent = 0;
        RefreshUpdateButton();
        var path = Path.Combine(Path.GetTempPath(), $"Krono-{Guid.NewGuid():N}-Setup.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        try {
            using var response = await UpdateHttp.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                var buffer = new byte[65536];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0) {
                    received += count;
                    if (received > update.Size) throw new InvalidDataException("Unexpected update size.");
                    hash.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), token);
                    var percent = (int)(100 * received / update.Size);
                    if (percent != updateDownloadPercent) {
                        updateDownloadPercent = percent;
                        if (settingsUpdateProgress is { } progress) progress.Value = percent;
                    }
                }
                if (received != update.Size || Convert.ToHexString(hash.GetHashAndReset()) != update.Hash)
                    throw new InvalidDataException("The installer could not be verified.");
            }
            return path;
        } catch {
            DeleteDownload(path);
            throw;
        }
    }

    static void DeleteDownload(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
