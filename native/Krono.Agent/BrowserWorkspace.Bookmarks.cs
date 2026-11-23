using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class BrowserWorkspace
{
    readonly StackPanel bookmarkItems = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    readonly MenuFlyout bookmarkOverflowMenu = new();
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
        bookmarkAction = ToolbarButton("Add or remove bookmark", Symbol.Favorite, ToggleCurrentBookmark);
        bookmarkOverflow = ToolbarButton("More bookmarks", Symbol.More);
        bookmarkOverflow.Content = new TextBlock { Text = "»", FontSize = 17, LineHeight = 20 };
        bookmarkOverflow.Flyout = bookmarkOverflowMenu;
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
        var label = saved ? "Remove bookmark" : "Add bookmark";
        bookmarkAction.IsEnabled = address is not null;
        ToolTipService.SetToolTip(bookmarkAction, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bookmarkAction, label);
        bookmarkAction.Content = ToolbarIcon(saved ? Symbol.SolidStar : Symbol.Favorite);
    }

    void ToggleCurrentBookmark()
    {
        if (BookmarkAddress is not { } uri || Current is not { } view) return;
        var key = "browser.bookmark." + uri.AbsoluteUri;
        workspace.SetSetting(key, workspace.Setting(key).Length == 0 ? SerializeVisit(uri, view.CoreWebView2.DocumentTitle) : "");
        RefreshBookmarks();
        RefreshBookmarkAction();
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
            ToolTipService.SetToolTip(button, visit.Title);
            button.Click += (_, _) => OpenBookmark(visit);
            bookmarks.Add((visit, button));
        }
        LayoutBookmarks();
    }

    void OpenBookmark(BrowserVisit visit)
    {
        var privateTab = tabs.SelectedItem is TabViewItem selected && privateTabs.Contains(selected);
        if (Current is { } view && !ReferenceEquals(agentView, view) && navigators.TryGetValue(view, out var navigate))
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
        }
        bookmarkOverflow.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
    }
}
