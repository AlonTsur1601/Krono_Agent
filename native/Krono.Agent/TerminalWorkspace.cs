using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Text.Json.Nodes;
using Windows.System;
using Windows.UI.Core;

namespace Krono_Agent;

public sealed class TerminalWorkspace : Grid, IDisposable
{
    readonly AgentWorker worker;
    readonly TabView tabs = new() { VerticalAlignment = VerticalAlignment.Stretch };
    readonly Dictionary<string, Session> terminals = new();

    sealed class Session(TabViewItem tab, TextBox screen)
    {
        public TabViewItem Tab { get; } = tab;
        public TextBox Screen { get; } = screen;
        public TerminalBuffer Buffer { get; } = new();
        public bool Rendering { get; set; }
        public bool Ready { get; set; }
    }

    public TerminalWorkspace(AgentWorker worker)
    {
        this.worker = worker;
        KronoDesign.StyleTabs(tabs);
        Children.Add(KronoDesign.FrameTabContent(tabs, "KronoSidebar"));
        tabs.AddTabButtonClick += async (_, _) => await Create();
        tabs.SelectionChanged += (_, _) => {
            if (tabs.SelectedItem is TabViewItem { Content: TextBox screen })
                DispatcherQueue.TryEnqueue(() => screen.Focus(FocusState.Programmatic));
        };
        tabs.TabCloseRequested += async (_, args) => {
            var id = terminals.FirstOrDefault(entry => ReferenceEquals(entry.Value.Tab, args.Tab)).Key;
            if (id is null || !terminals.Remove(id, out var terminal)) return;
            tabs.TabItems.Remove(args.Tab);
            if (terminal.Ready) {
                try { await worker.Request(Guid.NewGuid().ToString(), "terminal:close", new { id }); }
                catch (InvalidOperationException) { /* The shell may have exited while closing. */ }
            }
        };
        Loaded += async (_, _) => { if (tabs.TabItems.Count == 0) await Create(); };
    }

    async Task Create()
    {
        var id = Guid.NewGuid().ToString();
        var screen = new KronoTextBox { IsReadOnly = true, AcceptsReturn = true, PressMotionEnabled = false,
            FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize, TextWrapping = TextWrapping.NoWrap,
            SurfaceFeedbackEnabled = false, UseSystemFocusVisuals = false,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            Background = KronoDesign.ThemeBrush("KronoSurface", ActualTheme), VerticalContentAlignment = VerticalAlignment.Top };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(screen, "TerminalOutput");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(screen, "PowerShell terminal");
        screen.ActualThemeChanged += (_, _) => screen.Background = KronoDesign.ThemeBrush("KronoSurface", screen.ActualTheme);
        KronoInputMotion.RegisterPaletteRefresh(screen, () => screen.Background = KronoDesign.ThemeBrush("KronoSurface", screen.ActualTheme));
        var tab = new TabViewItem { Header = "PowerShell", Content = screen };
        KronoDesign.StyleTabHeader(tab);
        var terminal = new Session(tab, screen);
        screen.BeforeTextChanging += (_, args) => args.Cancel = !terminal.Rendering;
        screen.AddHandler(UIElement.CharacterReceivedEvent, new TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(async (_, args) => {
            if (char.IsControl(args.Character)) return;
            args.Handled = true;
            await Write(id, terminal, args.Character.ToString());
        }), true);
        screen.Paste += async (_, args) => {
            args.Handled = true;
            try {
                var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    await Write(id, terminal, (await clipboard.GetTextAsync()).Replace("\r\n", "\r").Replace('\n', '\r'));
            } catch (Exception error) { ShowError(terminal, error); }
        };
        screen.PreviewKeyDown += async (_, args) => {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            var input = (args.Key, ctrl, shift) switch {
                (VirtualKey.C, true, _) when screen.SelectionLength == 0 => "\u0003",
                (VirtualKey.L, true, _) => "\u000c",
                (VirtualKey.Enter, false, _) => "\r",
                (VirtualKey.Back, false, _) => "\u007f",
                (VirtualKey.Delete, false, _) => "\u001b[3~",
                (VirtualKey.Tab, false, false) => "\t",
                (VirtualKey.Tab, false, true) => "\u001b[Z",
                (VirtualKey.Escape, false, _) => "\u001b",
                (VirtualKey.Left, false, false) => "\u001b[D",
                (VirtualKey.Right, false, false) => "\u001b[C",
                (VirtualKey.Up, false, false) => "\u001b[A",
                (VirtualKey.Down, false, false) => "\u001b[B",
                (VirtualKey.Home, false, false) => "\u001b[H",
                (VirtualKey.End, false, false) => "\u001b[F",
                _ => null
            };
            if (input is null) return;
            args.Handled = true;
            await Write(id, terminal, input);
        };
        terminals[id] = terminal;
        tabs.TabItems.Add(tab);
        tabs.SelectedItem = tab;
        try {
            await worker.Request(id, "terminal:create", new { });
            if (!terminals.ContainsKey(id)) {
                await worker.Request(Guid.NewGuid().ToString(), "terminal:close", new { id });
                return;
            }
            terminal.Ready = true;
            screen.IsReadOnly = false;
            if (ReferenceEquals(tabs.SelectedItem, tab)) screen.Focus(FocusState.Programmatic);
        } catch (Exception error) { ShowError(terminal, error); }
    }

    async Task Write(string id, Session terminal, string input)
    {
        if (!terminal.Ready || !terminals.ContainsKey(id)) return;
        try { await worker.Request(Guid.NewGuid().ToString(), "terminal:write", new { id, content = input }); }
        catch (Exception error) { if (terminals.ContainsKey(id)) ShowError(terminal, error); }
    }

    static void ShowError(Session terminal, Exception error)
    {
        terminal.Buffer.Write("\r\n" + error.Message + "\r\n");
        Render(terminal);
    }

    static void Render(Session terminal)
    {
        var (text, caret) = terminal.Buffer.Render();
        terminal.Rendering = true;
        try {
            terminal.Screen.Text = text;
            terminal.Screen.Select(caret, 0);
        } finally { terminal.Rendering = false; }
    }

    public void Receive(string id, string type, JsonNode? data)
    {
        if (!terminals.TryGetValue(id, out var terminal)) return;
        if (type == "terminal") {
            terminal.Buffer.Write(data?.ToString() ?? "");
            Render(terminal);
        } else if (type == "terminal-exit") {
            terminal.Ready = false;
            terminal.Screen.IsReadOnly = true;
            terminal.Tab.Header = "PowerShell · exited";
        }
    }

    public void Dispose() { terminals.Clear(); tabs.TabItems.Clear(); }
}
