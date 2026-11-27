using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    FrameworkElement? projectsOverview;
    StackPanel? projectRows;
    readonly Dictionary<string, (string Name, Grid Row)> projectRowById = [];
    readonly TextBlock emptyProjects = new() { Text = "No projects created." };
    const string NewChatProjectMode = "native.newChatProjectMode";
    const string FixedNewChatProject = "native.newChatProjectId";
    const string LastActiveProject = "native.lastActiveProject";

    void RememberActiveProject(string project)
    {
        if (workspace.Setting(LastActiveProject) != project)
            workspace.SetSetting(LastActiveProject, project);
    }

    string? ResolveNewChatProject()
    {
        var id = workspace.Setting(NewChatProjectMode) switch {
            "none" => null,
            "current" => ComposerProject,
            "fixed" => workspace.Setting(FixedNewChatProject),
            _ => workspace.Setting(LastActiveProject) is { Length: > 0 } last ? last :
                workspace.Query("""
                    SELECT p.id FROM projects p JOIN chats c ON c.project_id=p.id
                    WHERE p.account_id=$p0 AND p.deleted_at IS NULL AND c.deleted_at IS NULL
                      AND NOT EXISTS (SELECT 1 FROM side_chats sc WHERE sc.chat_id=c.id)
                    ORDER BY c.updated_at DESC,c.id LIMIT 1
                    """, workspace.AccountId).FirstOrDefault()?["id"]?.ToString()
        };
        if (string.IsNullOrEmpty(id) || workspace.Query(
            "SELECT id FROM projects WHERE id=$p0 AND account_id=$p1 AND deleted_at IS NULL", id, workspace.AccountId).Count == 0)
            return null;
        return id;
    }

    void AddNewChatProjectSettings(StackPanel panel)
    {
        var modes = new[] { ("last", "Last active project"), ("none", "No project"),
            ("current", "Current project"), ("fixed", "Choose a project") };
        var projects = OrderedProjects();
        var selected = Array.FindIndex(modes, mode => mode.Item1 == workspace.Setting(NewChatProjectMode));
        var fixedProject = new KronoComboBox {
            Header = "Default project", PlaceholderText = "Choose a project",
            ItemsSource = projects.Select(project => project.Name).ToArray(),
            SelectedIndex = projects.FindIndex(project => project.Id == workspace.Setting(FixedNewChatProject)),
            Visibility = selected == 3 ? Visibility.Visible : Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(fixedProject, "FixedNewChatProject");
        fixedProject.SelectionChanged += (_, _) => {
            if (fixedProject.SelectedIndex >= 0)
                workspace.SetSetting(FixedNewChatProject, projects[fixedProject.SelectedIndex].Id);
        };
        var picker = Choices("New chat project", modes.Select(mode => mode.Item2).ToArray(),
            modes[Math.Max(0, selected)].Item2, name => {
                var mode = modes.First(item => item.Item2 == name).Item1;
                workspace.SetSetting(NewChatProjectMode, mode);
                fixedProject.Visibility = mode == "fixed" ? Visibility.Visible : Visibility.Collapsed;
            });
        panel.Children.Add(picker);
        panel.Children.Add(fixedProject);
    }

    List<(string Id, string Name)> OrderedProjects()
    {
        var positions = OrderPositions("native.projectOrder");
        return workspace.Query("SELECT id,name FROM projects WHERE account_id=$p0 AND deleted_at IS NULL ORDER BY name COLLATE NOCASE,id", workspace.AccountId)
            .Select(row => (Id: row["id"]!.ToString(), Name: row["name"]!.ToString()))
            .OrderBy(project => positions.GetValueOrDefault(project.Id, int.MaxValue)).ToList();
    }

    void ShowProjects(string? selectedProject = null, bool editing = false)
    {
        if (backToChats is not null) backToChats.Visibility = Visibility.Visible;
        SetSideChatVisible(false);
        var page = new StackPanel { Spacing = 14 };
        if (editing) {
            page.Children.Add(Heading(selectedProject is null ? "Create project" : "Edit project"));
            AddProjectEditor(page, selectedProject);
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 780 });
            layout.Children.Add(page);
            KronoDesign.Present(Body, KronoDesign.ScrollPage(layout));
            return;
        }

        if (projectsOverview is not null) {
            RefreshProjectRows();
            if (!ReferenceEquals(Body.Content, projectsOverview)) KronoDesign.Present(Body, projectsOverview);
            return;
        }
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Heading("Projects"));
        var create = Button("New project", () => ShowProjects(editing: true));
        create.Style = (Style)Application.Current.Resources["KronoPrimaryButton"];
        Grid.SetColumn(create, 1);
        header.Children.Add(create);
        page.Children.Add(header);

        projectRows = new StackPanel { Spacing = 6 };
        AutomationProperties.SetAutomationId(projectRows, "ProjectRows");
        page.Children.Add(projectRows);
        RefreshProjectRows();
        projectsOverview = KronoDesign.ScrollPage(page);
        KronoDesign.Present(Body, projectsOverview);
    }

    void RefreshProjectRows()
    {
        if (projectRows is not { } rows) return;
        var projects = OrderedProjects();
        var ids = projects.Select(project => project.Id).ToHashSet();
        foreach (var id in projectRowById.Keys.Where(id => !ids.Contains(id)).ToArray()) {
            var row = projectRowById[id].Row;
            orderRows.Remove(row);
            rows.Children.Remove(row);
            projectRowById.Remove(id);
        }
        if (projects.Count == 0) {
            if (!rows.Children.Contains(emptyProjects)) rows.Children.Add(emptyProjects);
            return;
        }
        rows.Children.Remove(emptyProjects);
        for (var index = 0; index < projects.Count; index++) {
            var (id, name) = projects[index];
            if (!projectRowById.TryGetValue(id, out var existing) || existing.Name != name) {
                if (existing.Row is not null) {
                    orderRows.Remove(existing.Row);
                    rows.Children.Remove(existing.Row);
                }
                existing = (name, BuildProjectRow(id, name));
                projectRowById[id] = existing;
            }
            var previous = rows.Children.IndexOf(existing.Row);
            if (previous == index) continue;
            if (previous >= 0) rows.Children.RemoveAt(previous);
            rows.Children.Insert(index, existing.Row);
        }
    }

    Grid BuildProjectRow(string id, string name)
    {
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(14, 9, 14, 9), MinHeight = 58, CornerRadius = new CornerRadius(10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool hovered = false;
        void PaintRow() => row.Background = KronoDesign.ThemeBrush(hovered ? "KronoInputFace" : "KronoSurface", row.ActualTheme);
        PaintRow();
        row.PointerEntered += (_, _) => { hovered = true; PaintRow(); };
        row.PointerExited += (_, _) => { hovered = false; PaintRow(); };
        row.ActualThemeChanged += (_, _) => PaintRow();
        var icon = KronoDesign.OutlineIcon(KronoDesign.OutlineGlyph.Folder);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var title = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        var label = new Grid { ColumnSpacing = 10 };
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition());
        label.Children.Add(icon);
        label.Children.Add(title);
        var open = Button("", () => AfterOrderGesture(() => OpenRecentProjectChat(id)));
        open.Name = "ProjectOpenButton";
        open.Content = label;
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        open.Padding = new Thickness(8, 4, 8, 4);
        AutomationProperties.SetName(open, name);
        AutomationProperties.SetAutomationId(open, "ProjectRow-" + id);
        ToolTipService.SetToolTip(open, name);
        open.Template = (ControlTemplate)Application.Current.Resources["KronoFlatIconTemplate"];
        Grid.SetColumnSpan(open, 2);
        row.Children.Add(open);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var edit = Button("Edit", () => ShowProjects(id, editing: true));
        AutomationProperties.SetName(edit, "Edit project");
        var delete = Button("Delete", () => DeleteProject(id));
        AutomationProperties.SetName(delete, "Delete project…");
        KronoDesign.MarkDestructive(delete);
        EnableOrderDrag(row, () => new RailEntry(RailKind.Disclosure, name, id, Project: true));
        var addChat = HistoryAction("New chat in " + name, new SymbolIcon(Symbol.Add), () => NewProjectChat(id));
        actions.Children.Add(addChat);
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    void OpenRecentProjectChat(string project)
    {
        if (runId is not null) return;
        var recent = workspace.Query("""
            SELECT c.id FROM chats c
            WHERE c.account_id=$p0 AND c.project_id=$p1 AND c.deleted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM side_chats s WHERE s.chat_id=c.id)
              AND EXISTS (SELECT 1 FROM messages m WHERE m.chat_id=c.id AND m.deleted_at IS NULL)
            ORDER BY (SELECT MAX(m.created_at) FROM messages m
                WHERE m.chat_id=c.id AND m.deleted_at IS NULL) DESC, c.updated_at DESC, c.id
            LIMIT 1
            """, workspace.AccountId, project).FirstOrDefault()?["id"]?.ToString();
        if (recent is null) {
            NewProjectChat(project);
            return;
        }
        chatId = recent;
        collapsedChatProjects.Remove(project);
        ShowChats();
    }

    void AddProjectEditor(StackPanel page, string? id)
    {
        var name = new KronoTextBox {
            Header = "Project name",
            Text = id is null ? "" : workspace.Query("SELECT name FROM projects WHERE id=$p0 AND deleted_at IS NULL", id)
                .FirstOrDefault()?["name"]?.ToString() ?? ""
        };
        AutomationProperties.SetAutomationId(name, "ProjectName");
        page.Children.Add(name);
        var folders = id is null ? new List<string>() : workspace.Query(
            "SELECT path FROM project_directories WHERE project_id=$p0 ORDER BY path", id)
            .Select(row => row["path"]!.ToString()).ToList();
        page.Children.Add(new TextBlock { Text = "Source folders", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var rows = new StackPanel { Spacing = 6 };
        void RefreshFolders()
        {
            rows.Children.Clear();
            foreach (var path in folders) {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                var remove = Button("Remove folder", () => { folders.Remove(path); RefreshFolders(); });
                KronoDesign.MarkDestructive(remove);
                row.Children.Add(remove);
                Grid.SetColumn(remove, 1);
                rows.Children.Add(row);
            }
        }
        page.Children.Add(rows);
        RefreshFolders();
        page.Children.Add(Button("Add folder…", async () => {
            try {
                var path = await PickProjectFolder();
                if (path is not null && !folders.Contains(path, StringComparer.OrdinalIgnoreCase)) {
                    folders.Add(path);
                    RefreshFolders();
                }
            } catch (Exception error) { Error(error); }
        }));
        var permission = id is null ? "ask" : workspace.ProjectPreferences(id)["permissions"]?.ToString() ?? "ask";
        var permissions = Choices("Default permissions", ["ask", "project", "full"], permission, value => permission = value);
        permissions.SelectionChanged += (_, _) => MarkFullAccess(permissions, permission);
        if (id is not null) page.Children.Add(KronoDesign.Section("Permissions", permissions));
        async Task Save()
        {
            try {
                var title = name.Text.Trim();
                if (title.Length == 0) throw new InvalidOperationException("Give the project a name.");
                var generation = sessionGeneration;
                if (permission == "full" && (id is null || workspace.ProjectPreferences(id)["permissions"]?.ToString() != "full") &&
                    (!await Confirm("Enable full access for this project?", "Tools in this project can run commands and change files with your Windows permissions without asking.", "Enable full access") ||
                     generation != sessionGeneration)) return;
                var project = id ?? Guid.NewGuid().ToString();
                var config = workspace.ProjectPreferences(project);
                config["permissions"] = permission;
                workspace.Execute("BEGIN");
                try {
                    workspace.Execute("""
                        INSERT INTO projects(id,account_id,name,created_at,updated_at) VALUES($p0,$p1,$p2,$p3,$p3)
                        ON CONFLICT(id) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at
                        """, project, workspace.AccountId, title, Workspace.Now);
                    workspace.Execute("DELETE FROM project_directories WHERE project_id=$p0 AND device_id='local'", project);
                    foreach (var directory in folders)
                        workspace.Execute("INSERT INTO project_directories(id,project_id,device_id,path) VALUES($p0,$p1,'local',$p2)",
                            Guid.NewGuid().ToString(), project, directory);
                    workspace.SetSetting("project." + project, config.ToJsonString());
                    workspace.Execute("COMMIT");
                } catch { workspace.Execute("ROLLBACK"); throw; }
                ReloadChats();
                ShowProjects();
            } catch (Exception error) { Error(error); }
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(Button("Cancel", () => ShowProjects()));
        var save = Button("Save project", Save);
        save.Style = (Style)Application.Current.Resources["KronoPrimaryButton"];
        actions.Children.Add(save);
        page.Children.Add(actions);
    }

    async Task DeleteProject(string id)
    {
        var account = workspace.AccountId;
        var generation = sessionGeneration;
        var title = workspace.Query("SELECT name FROM projects WHERE id=$p0 AND account_id=$p1 AND deleted_at IS NULL", id, account)
            .FirstOrDefault()?["name"]?.ToString();
        if (title is null || !await Confirm($"Delete {title}?",
                "Conversations will move out of this project, scheduled tasks linked to it will pause, and files on disk will be kept.",
                "Delete project", destructive: true) || generation != sessionGeneration) return;

        var now = Workspace.Now;
        workspace.Execute("BEGIN");
        try {
            workspace.Execute("UPDATE scheduled_tasks SET state='paused' WHERE account_id=$p0 AND project_id=$p1 AND state='active'", account, id);
            workspace.Execute("UPDATE chats SET project_id=NULL,updated_at=$p0 WHERE account_id=$p1 AND project_id=$p2 AND deleted_at IS NULL", now, account, id);
            workspace.Execute("UPDATE projects SET deleted_at=$p0,updated_at=$p0 WHERE id=$p1 AND account_id=$p2 AND deleted_at IS NULL", now, id, account);
            workspace.Execute("COMMIT");
        } catch {
            workspace.Execute("ROLLBACK");
            throw;
        }
        if (draftProject == id) draftProject = null;
        ReloadChats();
        RefreshProjectRows();
        ShowNotice($"Deleted {title}.");
    }

    async Task<string?> PickProjectFolder()
    {
        var input = new KronoTextBox { Header = "Folder path", PlaceholderText = "Enter an absolute path" };
        AutomationProperties.SetAutomationId(input, "ProjectFolderPath");
        var dialog = new KronoDialog {
            XamlRoot = XamlRoot, Title = "Add project folder", Content = input,
            PrimaryButtonText = "Add folder", SecondaryButtonText = "Browse…", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false
        };
        string? ValidatePath()
        {
            try {
                var value = input.Text.Trim();
                return Path.IsPathFullyQualified(value) && Directory.Exists(value) ? Path.GetFullPath(value) : null;
            } catch (ArgumentException) { return null; }
        }
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = ValidatePath() is not null;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) return ValidatePath();
        if (result != ContentDialogResult.Secondary) return null;
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance!));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    Grid BuildInstructionsSettings(ContentControl host)
    {
        var list = new StackPanel { Spacing = 22 };
        list.Children.Add(Heading("Instructions"));
        var overview = KronoDesign.ScrollPage(list);
        StackPanel Group(string title)
        {
            var group = new StackPanel { Spacing = 10 };
            group.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            list.Children.Add(group);
            return group;
        }
        void Edit(string title, string? project)
        {
            var editor = new Grid { RowSpacing = 14, Margin = new Thickness(28) };
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.RowDefinitions.Add(new RowDefinition());
            editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editor.Children.Add(Heading("Instructions · " + title));
            var field = new KronoTextBox {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PressMotionEnabled = false,
                Text = project is null ? workspace.Setting("instructions.global") :
                    workspace.ProjectPreferences(project)["instructions"]?.ToString() ?? ""
            };
            AutomationProperties.SetAutomationId(field, project is null ? "GlobalInstructions" : "ProjectInstructions");
            var input = NativeMarkdown.Editor(field, workspace);
            Grid.SetRow(input, 1);
            editor.Children.Add(input);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            void Return() => KronoDesign.Present(host, overview);
            actions.Children.Add(Button("Save instructions", () => {
                if (project is null) workspace.SetSetting("instructions.global", field.Text.Trim());
                else {
                    var config = workspace.ProjectPreferences(project);
                    config["instructions"] = field.Text.Trim();
                    workspace.SetSetting("project." + project, config.ToJsonString());
                }
                Return();
            }));
            actions.Children.Add(Button("Cancel", Return));
            Grid.SetRow(actions, 2);
            editor.Children.Add(actions);
            KronoDesign.Present(host, editor);
        }
        Group("Global").Children.Add(Button("Edit global instructions", () => Edit("Global", null)));
        var projects = Group("Projects");
        foreach (var row in workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name,id")) {
            var name = row["name"]!.ToString();
            var id = row["id"]!.ToString();
            projects.Children.Add(Button(name, () => Edit(name, id)));
        }
        if (projects.Children.Count == 1) projects.Children.Add(new TextBlock { Text = "No projects created." });
        return overview;
    }
}
