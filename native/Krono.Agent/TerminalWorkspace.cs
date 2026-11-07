using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System.Text.Json.Nodes;

namespace Krono_Agent;

public sealed class TerminalWorkspace : Grid, IDisposable
{
    readonly AgentWorker worker;
    readonly TabView tabs = new() { VerticalAlignment = VerticalAlignment.Stretch };
    readonly Dictionary<string, TerminalBuffer> buffers = new();
    readonly Dictionary<string, (TabViewItem Tab, TextBox Output)> terminals = new();
    readonly KronoTextBox input = new() { PlaceholderText = "PowerShell input · Enter to send", FontFamily = KronoDesign.CodeFont };
    public TerminalWorkspace(AgentWorker worker, DispatcherQueue queue)
    {
        this.worker = worker;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "TerminalInput");
        RowSpacing = 10; RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void SetTabPalette()
        {
            var surface = KronoDesign.ThemeBrush("KronoSurface", ActualTheme);
            tabs.Resources["TabViewItemHeaderBackgroundSelected"] = surface;
            tabs.Resources["TabViewBorderBrush"] = surface;
        }
        SetTabPalette();
        ActualThemeChanged += (_, _) => SetTabPalette();
        Children.Add(tabs);
        var commands = new Grid { ColumnSpacing = 8 };
        commands.ColumnDefinitions.Add(new ColumnDefinition());
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commands.Children.Add(input);
        var send = new Button { Content = "Run input" }; send.Click += async (_, _) => await WriteInput(); Grid.SetColumn(send, 1); commands.Children.Add(send);
        var interrupt = new Button { Content = "Interrupt" }; interrupt.Click += async (_, _) => { var id = SelectedId(); if (id is not null) await worker.Request(Guid.NewGuid().ToString(), "terminal:write", new { id, content = "\u0003" }); }; Grid.SetColumn(interrupt, 2); commands.Children.Add(interrupt);
        Grid.SetRow(commands, 1); Children.Add(commands);
        tabs.AddTabButtonClick += async (_, _) => await Create();
        tabs.TabCloseRequested += async (_, args) => { var entry = terminals.FirstOrDefault(t => t.Value.Tab == args.Tab); if (entry.Key is not null) { await worker.Request(Guid.NewGuid().ToString(), "terminal:close", new { id = entry.Key }); terminals.Remove(entry.Key); tabs.TabItems.Remove(args.Tab); } };
        input.KeyDown += async (_, args) => {
            var id = terminals.FirstOrDefault(t => ReferenceEquals(t.Value.Tab, tabs.SelectedItem)).Key; if (id is null) return;
            if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await WriteInput(); }
        };
        Loaded += async (_, _) => { if (tabs.TabItems.Count == 0) await Create(); };
    }
    string? SelectedId() => terminals.FirstOrDefault(t => ReferenceEquals(t.Value.Tab, tabs.SelectedItem)).Key;
    async Task WriteInput() { var id = SelectedId(); if (id is null) return; var text = input.Text; input.Text = ""; await worker.Request(Guid.NewGuid().ToString(), "terminal:write", new { id, content = text + "\r" }); }
    async Task Create()
    {
        var id = Guid.NewGuid().ToString();
        var output = new KronoTextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize, TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0, 0, 10, 10) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(output, "TerminalOutput");
        var tab = new TabViewItem { Header = "PowerShell", Content = output }; terminals[id] = (tab, output); buffers[id] = new TerminalBuffer(); tabs.TabItems.Add(tab); tabs.SelectedItem = tab;
        try { await worker.Request(id, "terminal:create", new { }); } catch (Exception error) { output.Text = error.Message; }
    }
    public void Receive(string id, string type, JsonNode? data)
    {
        if (!terminals.TryGetValue(id, out var terminal)) return;
        if (type == "terminal") {
            buffers[id].Write(data?.ToString() ?? ""); terminal.Output.Text = buffers[id].ToString();
            terminal.Output.SelectionStart = terminal.Output.Text.Length;
        }
        if (type == "terminal-exit") terminal.Tab.Header = "PowerShell · exited";
    }
    public void Dispose() { terminals.Clear(); buffers.Clear(); tabs.TabItems.Clear(); }
}
