using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    const string ReleaseRoot = "https://github.com/AlonTsur1601/Krono_Agent/releases";
    const string InstallerName = "Krono-Agent-Setup-x64.exe";
    static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromDays(1) };
    readonly CancellationTokenSource updateLifetime = new();
    readonly record struct AvailableUpdate(Version Version, Uri Url, long Size, string Hash);
    AvailableUpdate? availableUpdate;
    string? downloadedInstaller;
    bool checkingUpdate, downloadingUpdate;
    static Version InstalledVersion => typeof(MainPage).Assembly.GetName().Version ?? new Version(0, 0);
    internal bool HasBackgroundWork => runId is not null || scheduledRun is not null ||
        sideChat?.IsRunning == true || downloadingUpdate;

    void InitializeUpdates()
    {
        Loaded += async (_, _) => { updateTimer.Start(); await CheckUpdates(); };
        updateTimer.Tick += async (_, _) => await CheckUpdates();
        Unloaded += (_, _) => { updateTimer.Stop(); updateLifetime.Cancel(); DeleteDownload(downloadedInstaller); };
        Nav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => RefreshUpdateButton());
    }

    void AddUpdateSettings(StackPanel panel)
    {
        var version = InstalledVersion.ToString(3);
        panel.Children.Add(new TextBlock { Text = "Krono Agent " + version });
        panel.Children.Add(Button("Check for updates", async () => await CheckUpdates(true)));
        panel.Children.Add(new HyperlinkButton { Content = "Download updates", NavigateUri = new Uri(ReleaseRoot + "/latest") });
    }

    async Task CheckUpdates(bool manual = false)
    {
        if (checkingUpdate || downloadingUpdate || downloadedInstaller is not null) return;
        checkingUpdate = true;
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/AlonTsur1601/Krono_Agent/releases/latest");
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
                if (manual) { Notice.Severity = InfoBarSeverity.Success; Notice.Message = "Krono Agent is up to date."; Notice.IsOpen = true; }
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
                return;
            }
            throw new InvalidDataException("The release has no verified Windows installer.");
        } catch (Exception error) {
            if (manual && !updateLifetime.IsCancellationRequested) Error(error);
        } finally { checkingUpdate = false; RefreshUpdateButton(); }
    }

    void RefreshUpdateButton()
    {
        UpdateButton.Visibility = availableUpdate.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Visibility = downloadingUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.IsEnabled = !downloadingUpdate;
        var label = downloadedInstaller is not null ? "Install update" : downloadingUpdate ? "Downloading…" : "Download update";
        UpdateLabel.Text = label;
        UpdateLabel.Visibility = Nav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(UpdateButton, label);
        ToolTipService.SetToolTip(UpdateButton, label);
    }

    async void UpdateAction(object sender, RoutedEventArgs args)
    {
        if (downloadingUpdate || availableUpdate is not { } update) return;
        if (downloadedInstaller is { } installer) {
            if (HasBackgroundWork && !await Confirm("Install update?", "Krono Agent needs to close to install. Running work will be interrupted.", "Install update")) return;
            try {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                downloadedInstaller = null; // The setup process still needs the file.
                MainWindow.Instance?.CloseApproved();
            } catch (Exception error) { Error(error); }
            return;
        }

        downloadingUpdate = true;
        UpdateProgress.Value = 0;
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
                    UpdateProgress.Value = 100.0 * received / update.Size;
                }
                if (received != update.Size || Convert.ToHexString(hash.GetHashAndReset()) != update.Hash)
                    throw new InvalidDataException("The installer could not be verified.");
            }
            downloadedInstaller = path;
        } catch (Exception error) {
            DeleteDownload(path);
            if (!updateLifetime.IsCancellationRequested) Error(error);
        } finally { downloadingUpdate = false; RefreshUpdateButton(); }
    }

    static void DeleteDownload(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
