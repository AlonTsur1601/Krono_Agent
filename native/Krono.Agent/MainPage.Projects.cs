using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Krono_Agent;

public sealed partial class MainPage
{
    void ShowProjects(string? selectedProject = null, bool editing = false)
    {
        if (backToChats is not null) backToChats.Visibility = Visibility.Visible;
        SetSideChatVisible(false);
        var projects = workspace.Query("SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name,id");
        var page = new StackPanel { Spacing = 14, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(Heading("Projects"));
        if (projects.Count == 0 && !editing) {
            page.Children.Add(new TextBlock { Text = "No projects created.", TextWrapping = TextWrapping.Wrap });
            page.Children.Add(Button("New project", () => ShowProjects(editing: true)));
        } else if (editing) {
            AddProjectEditor(page, selectedProject);
        } else {
            var picker = new KronoComboBox {
                Header = "Project", ItemsSource = projects.Select(row => row["name"]!.ToString()).ToArray()
            };
            AutomationProperties.SetAutomationId(picker, "ProjectPicker");
            var summary = new StackPanel { Spacing = 7 };
            void ShowSelection()
            {
                if (picker.SelectedIndex < 0) return;
                var id = projects[picker.SelectedIndex]["id"]!.ToString();
                summary.Children.Clear();
                var folders = workspace.Query("SELECT path FROM project_directories WHERE project_id=$p0 ORDER BY path", id);
                if (folders.Count == 0) summary.Children.Add(new TextBlock { Text = "No project folders." });
                foreach (var folder in folders)
                    summary.Children.Add(new TextBlock { Text = folder["path"]!.ToString(), TextWrapping = TextWrapping.Wrap });
            }
            picker.SelectionChanged += (_, _) => ShowSelection();
            picker.SelectedIndex = Math.Max(0, projects.FindIndex(row => row["id"]!.ToString() == selectedProject));
            page.Children.Add(picker);
            page.Children.Add(summary);
            page.Children.Add(Button("Edit project", () => {
                if (picker.SelectedIndex >= 0) ShowProjects(projects[picker.SelectedIndex]["id"]!.ToString(), editing: true);
            }));
            page.Children.Add(Button("New project", () => ShowProjects(editing: true)));
        }
        KronoDesign.Present(Body, KronoDesign.ScrollPage(page));
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
                ShowProjects(project);
            } catch (Exception error) { Error(error); }
        }
        page.Children.Add(Button("Save project", async () => await Save()));
        page.Children.Add(Button("Cancel", () => ShowProjects(id)));
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
