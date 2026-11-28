using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    sealed record Destination(string Section, string? Item = null, string? Project = null, bool Editing = false);
    readonly List<Destination> navigationHistory = [];
    int historyIndex = -1;
    bool restoringNavigation;

    void RememberNavigation(Destination next)
    {
        if (restoringNavigation || workspace.AccountId.Length == 0 ||
            historyIndex >= 0 && navigationHistory[historyIndex] == next) return;
        var firstForward = historyIndex + 1;
        navigationHistory.RemoveRange(firstForward, navigationHistory.Count - firstForward);
        navigationHistory.Add(next);
        if (navigationHistory.Count > 100) navigationHistory.RemoveAt(0);
        historyIndex = navigationHistory.Count - 1;
        UpdateNavigationButtons();
    }

    void UpdateNavigationButtons() =>
        MainWindow.Instance?.UpdateNavigationButtons(historyIndex > 0, historyIndex + 1 < navigationHistory.Count);

    void ResetNavigationHistory()
    {
        navigationHistory.Clear();
        historyIndex = -1;
        UpdateNavigationButtons();
    }

    internal bool NavigateHistory(int direction)
    {
        var index = historyIndex + direction;
        if (workspace.AccountId.Length == 0 || index < 0 || index >= navigationHistory.Count) return false;
        var target = navigationHistory[index];
        if (target.Section == "chats" && target.Item != chatId && runId is not null) return false;
        restoringNavigation = true;
        try {
            var section = target.Section == "projects" ? "chats" : target.Section;
            var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()
                .First(entry => entry.Tag as string == section);
            var selected = ReferenceEquals(Nav.SelectedItem, item);
            if (target.Section == "chats") {
                chatId = target.Item is { } chat && workspace.Query(
                    "SELECT id FROM chats WHERE id=$p0 AND account_id=$p1 AND deleted_at IS NULL", chat, workspace.AccountId).Count > 0
                    ? chat : null;
                draftProject = target.Project;
                if (selected) ShowChats();
            }
            if (section == "settings") {
                if (selected) ShowSettings(target.Item);
                else nextSettingsCategory = target.Item;
            }
            if (!selected) Nav.SelectedItem = item;
            if (target.Section == "projects") {
                var projectExists = target.Item is null || OrderedProjects().Any(project => project.Id == target.Item);
                ShowProjects(target.Item, target.Editing && projectExists);
            }
            historyIndex = index;
            UpdateNavigationButtons();
            return true;
        } catch (Exception error) {
            Error(error);
            return false;
        } finally {
            restoringNavigation = false;
        }
    }
}
