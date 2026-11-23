using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using System.Globalization;
using System.Text.Json;
using Windows.Storage.Streams;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    sealed record BrowserVisit(string Url, string Title);
    const uint MaxFaviconBytes = 32 * 1024;
    static string SerializeVisit(Uri uri, string title)
        => JsonSerializer.Serialize(new BrowserVisit(uri.AbsoluteUri, string.IsNullOrWhiteSpace(title) ? uri.Host : title));

    static string FaviconKey(Uri uri) => "browser.favicon." + uri.GetLeftPart(UriPartial.Authority);

    async Task RefreshFavicon(WebView2 view, TabViewItem tab, ContentControl tabIcon, bool isPrivate, int generation, Func<int> currentGeneration)
    {
        var account = workspace.AccountId;
        bool IsCurrent() => !disposed && workspace.AccountId == account && generation == currentGeneration() &&
            views.TryGetValue(tab, out var registered) && ReferenceEquals(registered, view);
        if (!IsCurrent() || account.Length == 0) return;
        BeginBrowserOperation(view);
        try {
            if (view.Source is not { Scheme: "http" or "https" } uri) return;
            using var image = await view.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (!IsCurrent() || image is null || image.Size is 0 or > MaxFaviconBytes) return;
            using var reader = new DataReader(image);
            var size = (uint)image.Size;
            if (await reader.LoadAsync(size) != size || !IsCurrent() || view.Source != uri) return;
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            var bitmap = await DecodeFavicon(bytes);
            if (!IsCurrent() || view.Source != uri) return;
            tabIcon.Content = new Image { Source = bitmap, Width = 16, Height = 16 };
            if (!isPrivate) workspace.SetSetting(FaviconKey(uri), Convert.ToBase64String(bytes));
        } catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        finally { EndBrowserOperation(view); }
    }

    static async Task<BitmapImage> DecodeFavicon(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    static BrowserVisit? ParseVisit(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var legacy) && legacy.Scheme is "http" or "https")
            return new BrowserVisit(legacy.AbsoluteUri, legacy.Host);
        try {
            var visit = JsonSerializer.Deserialize<BrowserVisit>(value);
            return visit is not null && Uri.TryCreate(visit.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? new BrowserVisit(uri.AbsoluteUri, string.IsNullOrWhiteSpace(visit.Title) ? uri.Host : visit.Title) : null;
        } catch (JsonException) { return null; }
    }

    void OpenHistory()
    {
        var privateContext = tabs.SelectedItem is TabViewItem selected && privateTabs.Contains(selected);
        var content = new StackPanel { Spacing = 8, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(22, 18, 22, 20) };
        var heading = new TextBlock { Text = "History", FontSize = 26 };
        AutomationProperties.SetAutomationId(heading, "BrowserHistory");
        content.Children.Add(heading);
        var visits = workspace.Query("""
            SELECT value, CAST(substr(key,17) AS INTEGER) AS visited_at
            FROM settings WHERE key GLOB 'browser.history.*' ORDER BY visited_at DESC LIMIT 200
            """).Select(row => (Visit: ParseVisit(row["value"]!.ToString()), Time: row["visited_at"]!.GetValue<long>()))
            .Where(row => row.Visit is not null && row.Time is > 0 and <= 253402300799999)
            .GroupBy(row => DateTimeOffset.FromUnixTimeMilliseconds(row.Time).LocalDateTime.Date).ToArray();
        if (visits.Length == 0) content.Children.Add(new TextBlock { Text = "No browsing history yet." });
        foreach (var day in visits) {
            content.Children.Add(new TextBlock {
                Text = day.Key.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture),
                Margin = new Thickness(0, 8, 0, 0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            foreach (var visit in day) content.Children.Add(HistoryRow(visit.Visit!, privateContext,
                DateTimeOffset.FromUnixTimeMilliseconds(visit.Time).ToLocalTime().ToString("t", CultureInfo.CurrentCulture)));
        }
        var page = new Grid { Visibility = Visibility.Collapsed };
        var scroll = new ScrollViewer { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SizeChanged += (_, args) => content.Width = Math.Max(0, Math.Min(820, args.NewSize.Width - 60));
        page.Children.Add(KronoDesign.ScrollPage(scroll, "KronoSurface"));
        var tab = new TabViewItem();
        CreateBrowserTabHeader(tab, "History", KronoDesign.NavigationIcon(Symbol.Clock));
        pages.Add(tab, page);
        pageHost.Children.Add(page);
        tabs.TabItems.Add(tab);
        tabs.SelectedItem = tab;
    }

    Button HistoryRow(BrowserVisit visit, bool privateContext, string time)
    {
        var uri = new Uri(visit.Url);
        var icon = new Grid { Width = 20, Height = 20, UseLayoutRounding = true, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(BrowserTabIcon());
        var encoded = workspace.Setting(FaviconKey(uri));
        var loading = false;
        if (encoded.Length is > 0 and <= 45000)
            icon.Loaded += async (_, _) => {
                if (loading || icon.Children.FirstOrDefault() is Image) return;
                loading = true;
                try {
                    var bitmap = await DecodeFavicon(Convert.FromBase64String(encoded));
                    icon.Children.Clear();
                    icon.Children.Add(new Image { Source = bitmap, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                } catch (Exception error) when (error is FormatException or System.Runtime.InteropServices.COMException) { }
                finally { loading = false; }
            };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(icon);
        var title = new TextBlock { Text = visit.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(title);
        Grid.SetColumn(title, 1);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var timestamp = new TextBlock { Text = time, Opacity = .65, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(timestamp, 2);
        row.Children.Add(timestamp);
        var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 6, 10, 6) };
        AutomationProperties.SetName(button, visit.Title + ", " + time);
        ToolTipService.SetToolTip(button, uri.Host);
        button.Click += (_, _) => _ = Open(visit.Url, privateContext);
        return button;
    }
}
