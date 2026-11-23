using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    readonly StackPanel bookmarkItems = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    readonly MenuFlyout bookmarkOverflowMenu = KronoDesign.CreateMenu();
    readonly List<(BrowserVisit Visit, Button Button)> bookmarks = new();
    Button? bookmarkOverflow;
    Button? bookmarkAction;
    Grid? bookmarkBar;

    Grid CreateBookmarkBar()
    {
        var bar = new Grid { ColumnSpacing = 4, Margin = new Thickness(6, 0, 6, 4), Height = 29 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bookmarkAction = ToolbarButton("Add or remove bookmark", Symbol.Favorite, EditCurrentBookmark);
        bookmarkOverflow = ToolbarButton("More bookmarks", Symbol.More);
        bookmarkOverflow.Content = new TextBlock { Text = "»", FontSize = 17, LineHeight = 20 };
        bookmarkOverflow.Click += (_, _) => bookmarkOverflowMenu.ShowAt(bookmarkOverflow);
        bar.Children.Add(bookmarkAction);
        bar.Children.Add(bookmarkItems);
        bar.Children.Add(bookmarkOverflow);
        Grid.SetColumn(bookmarkItems, 1);
        Grid.SetColumn(bookmarkOverflow, 2);
        bar.SizeChanged += (_, _) => LayoutBookmarks();
        bookmarkBar = bar;
        RefreshBookmarks();
        RefreshBookmarkAction();
        return bar;
    }

    Uri? BookmarkAddress => tabs.SelectedItem is TabViewItem tab && !privateTabs.Contains(tab) &&
        Current is { } view && readyViews.Contains(view) && !loadingViews.Contains(view) &&
        view.Source is { Scheme: "https" or "http" } uri ? uri : null;

    void RefreshBookmarkAction()
    {
        if (bookmarkAction is null) return;
        var address = BookmarkAddress;
        var saved = address is not null && bookmarks.Any(item => item.Visit.Url == address.AbsoluteUri);
        var label = saved ? "Edit bookmark" : "Add bookmark";
        bookmarkAction.IsEnabled = address is not null;
        ToolTipService.SetToolTip(bookmarkAction, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bookmarkAction, label);
        bookmarkAction.Content = ToolbarIcon(saved ? Symbol.SolidStar : Symbol.Favorite);
    }

    async void EditCurrentBookmark()
    {
        if (BookmarkAddress is not { } uri || Current is not { } view) return;
        var key = BookmarkKey(uri);
        var saved = ParseVisit(workspace.Setting(key));
        var title = view.CoreWebView2.DocumentTitle;
        await EditBookmark(saved is null ? null : key, saved ?? new BrowserVisit(uri.AbsoluteUri,
            string.IsNullOrWhiteSpace(title) ? uri.Host : title));
    }

    static string BookmarkKey(Uri uri) => "browser.bookmark." + uri.AbsoluteUri;

    bool editingBookmark;
    async Task EditBookmark(string? previousKey, BrowserVisit visit)
    {
        if (disposed || editingBookmark) return;
        editingBookmark = true;
        try {
            var name = new KronoTextBox { Header = "Name", Text = visit.Title };
            var link = new KronoTextBox { Header = "Link", Text = visit.Url };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(name, "BookmarkName");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, "BookmarkLink");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var dialog = new KronoDialog { XamlRoot = XamlRoot, Title = previousKey is null ? "Add bookmark" : "Edit bookmark",
                Content = new StackPanel { Spacing = 8, Children = { name, link, error } },
                PrimaryButtonText = "Save bookmark", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            bool TryRead(out Uri uri) => Uri.TryCreate(link.Text.Trim(), UriKind.Absolute, out uri!) &&
                CanAgentBrowse(uri) && !string.IsNullOrWhiteSpace(name.Text);
            dialog.PrimaryButtonClick += (_, args) => {
                if (!TryRead(out var uri)) { args.Cancel = true; error.Text = "Enter a name and a valid HTTP or HTTPS link."; }
                else if (BookmarkKey(uri) != previousKey && workspace.Setting(BookmarkKey(uri)).Length > 0) {
                    args.Cancel = true;
                    error.Text = "A bookmark already uses this link.";
                }
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || disposed || !TryRead(out var updated)) return;
            var key = BookmarkKey(updated);
            var value = SerializeVisit(updated, name.Text.Trim());
            if (previousKey is not null && previousKey != key)
                workspace.Execute("UPDATE OR REPLACE settings SET key=$p0, value=$p1 WHERE key=$p2", key, value, previousKey);
            else workspace.SetSetting(key, value);
            RefreshBookmarks();
            RefreshBookmarkAction();
        } catch (Exception error) { if (!disposed) owner.Error(error); }
        finally { editingBookmark = false; }
    }

    void RemoveBookmark(BrowserVisit visit)
    {
        if (disposed) return;
        workspace.Execute("DELETE FROM settings WHERE key=$p0", BookmarkKey(new Uri(visit.Url)));
        RefreshBookmarks();
        RefreshBookmarkAction();
    }

    void AddBookmarkActions(IList<MenuFlyoutItemBase> menu, BrowserVisit visit)
    {
        var edit = new MenuFlyoutItem { Text = "Edit bookmark" };
        edit.Click += async (_, _) => await EditBookmark(BookmarkKey(new Uri(visit.Url)), visit);
        var delete = new MenuFlyoutItem { Text = "Delete bookmark" };
        delete.Click += (_, _) => RemoveBookmark(visit);
        menu.Add(edit);
        menu.Add(delete);
    }

    void RefreshBookmarks()
    {
        bookmarks.Clear();
        bookmarkItems.Children.Clear();
        foreach (var row in workspace.Query("SELECT value FROM settings WHERE key GLOB 'browser.bookmark.*' AND value <> '' ORDER BY key LIMIT 100")) {
            if (ParseVisit(row["value"]!.ToString()) is not { } visit) continue;
            var button = new Button {
                Content = new TextBlock { Text = visit.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                MaxWidth = 140, MinHeight = 26, Padding = new Thickness(7, 2, 7, 2)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "BrowserBookmark" + bookmarks.Count);
            var context = KronoDesign.CreateMenu();
            AddBookmarkActions(context.Items, visit);
            button.ContextFlyout = context;
            ToolTipService.SetToolTip(button, visit.Title);
            button.Click += (_, _) => OpenBookmark(visit);
            bookmarks.Add((visit, button));
        }
        LayoutBookmarks();
    }

    void OpenBookmark(BrowserVisit visit)
    {
        var privateTab = tabs.SelectedItem is TabViewItem selected && privateTabs.Contains(selected);
        if (Current is { } view && tabs.SelectedItem is TabViewItem active && !agentTabs.Contains(active) && navigators.TryGetValue(view, out var navigate))
            _ = navigate(new Uri(visit.Url));
        else _ = Open(visit.Url, privateTab);
    }

    void LayoutBookmarks()
    {
        if (bookmarkBar is null || bookmarkOverflow is null) return;
        var available = Math.Max(0, bookmarkBar.ActualWidth - 31);
        var widths = bookmarks.Select(item => {
            item.Button.Measure(new Windows.Foundation.Size(140, 29));
            return item.Button.DesiredSize.Width + bookmarkItems.Spacing;
        }).ToArray();
        var overflow = widths.Sum() > available;
        if (overflow) available = Math.Max(0, available - 31);
        int visible = 0;
        while (visible < widths.Length && widths[visible] <= available) available -= widths[visible++];

        while (bookmarkItems.Children.Count > visible) bookmarkItems.Children.RemoveAt(bookmarkItems.Children.Count - 1);
        for (var index = bookmarkItems.Children.Count; index < visible; index++) bookmarkItems.Children.Add(bookmarks[index].Button);
        bookmarkOverflowMenu.Items.Clear();
        foreach (var item in bookmarks.Skip(visible)) {
            var entry = new MenuFlyoutItem { Text = item.Visit.Title };
            entry.Click += (_, _) => OpenBookmark(item.Visit);
            bookmarkOverflowMenu.Items.Add(entry);
            var actions = new MenuFlyoutSubItem { Text = "Manage " + item.Visit.Title };
            AddBookmarkActions(actions.Items, item.Visit);
            bookmarkOverflowMenu.Items.Add(actions);
        }
        bookmarkOverflow.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
    }
}
