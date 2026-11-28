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
    const string TerminalSurface = "KronoInputFace";
    readonly AgentWorker worker;
    readonly TabView tabs = new() { VerticalAlignment = VerticalAlignment.Stretch };
    readonly ContentControl selectedTerminal = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly Dictionary<string, Session> terminals = new();
    readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    readonly HashSet<string> closing = new();
    bool hasOpened;
    bool disposed;

    sealed class Session(TabViewItem tab, TextBox screen)
    {
        public TabViewItem Tab { get; } = tab;
        public TextBox Screen { get; } = screen;
        public TerminalBuffer Buffer { get; } = new();
        public bool Rendering { get; set; }
        public bool Ready { get; set; }
        public System.Text.StringBuilder? StartupInput { get; set; }
        public bool ScrollQueued { get; set; }
        public bool NeedsRender { get; set; }
        public bool AgentTranscript { get; init; }
    }

    public TerminalWorkspace(AgentWorker worker)
    {
        this.worker = worker;
        var frame = new Grid();
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition());
        frame.Children.Add(KronoDesign.CreateTabStrip(tabs, () => _ = Create()));
        frame.Children.Add(selectedTerminal);
        void PaintContent() => selectedTerminal.Background = KronoDesign.ThemeBrush(TerminalSurface, selectedTerminal.ActualTheme);
        selectedTerminal.Loaded += (_, _) => PaintContent();
        selectedTerminal.ActualThemeChanged += (_, _) => PaintContent();
        KronoInputMotion.RegisterPaletteRefresh(selectedTerminal, PaintContent);
        Grid.SetRow(selectedTerminal, 1);
        Children.Add(KronoDesign.FrameTabContent(frame, "KronoSidebar"));
        tabs.SelectionChanged += (_, _) => {
            var terminal = terminals.Values.FirstOrDefault(session => ReferenceEquals(session.Tab, tabs.SelectedItem));
            selectedTerminal.Content = terminal?.Screen;
            if (terminal is not null) {
                FocusInput(terminal);
                if (terminal.NeedsRender) Render(terminal);
                else ScrollOutputToEnd(terminal);
            }
        };
        renderTimer.Tick += (_, _) => {
            renderTimer.Stop();
            if (tabs.SelectedItem is TabViewItem { Tag: string id } && terminals.TryGetValue(id, out var terminal) && terminal.NeedsRender)
                Render(terminal);
        };
        tabs.TabCloseRequested += async (sender, args) => {
            if (args.Tab is not TabViewItem { Tag: string id } tab || !closing.Add(id)) return;
            await KronoDesign.FadeTabClose(tab);
            closing.Remove(id);
            if (disposed || !terminals.Remove(id, out var terminal)) return;
            tabs.TabItems.Remove(tab);
            if (tabs.TabItems.Count == 0) _ = Create();
            if (terminal.Ready) {
                try { await worker.Request(Guid.NewGuid().ToString(), "terminal:close", new { id }); }
                catch (Exception error) when (error is InvalidOperationException or IOException) { /* The shell may have exited while closing. */ }
            }
        };
        Loaded += async (_, _) => {
            if (disposed) return;
            var firstOpen = !hasOpened;
            hasOpened = true;
            if (firstOpen && tabs.TabItems.Count == 0) await Create();
            if (tabs.SelectedItem is TabViewItem { Tag: string id } && terminals.TryGetValue(id, out var terminal)) {
                if (terminal.NeedsRender) Render(terminal);
                else ScrollOutputToEnd(terminal);
                FocusInput(terminal);
            }
        };
        Unloaded += (_, _) => renderTimer.Stop();
    }

    void FocusInput(Session terminal)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
            if (IsLoaded && terminal.Screen.IsLoaded && !terminal.Screen.IsReadOnly &&
                ReferenceEquals(selectedTerminal.Content, terminal.Screen)) terminal.Screen.Focus(FocusState.Programmatic);
        });
    }

    Session NewSession(string id, string name, bool agent = false)
    {
        var screen = new KronoTextBox { IsReadOnly = agent, AcceptsReturn = true, PressMotionEnabled = false,
            FlowDirection = FlowDirection.LeftToRight, TextReadingOrder = TextReadingOrder.UseFlowDirection,
            TextAlignment = TextAlignment.Left, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false,
            FontFamily = KronoDesign.CodeFont, FontSize = KronoDesign.CodeSize, TextWrapping = TextWrapping.NoWrap,
            SurfaceFeedbackEnabled = false, UseSystemFocusVisuals = false,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            Background = KronoDesign.ThemeBrush(TerminalSurface, ActualTheme), VerticalContentAlignment = VerticalAlignment.Top };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(screen, "TerminalOutput");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(screen, agent ? "Agent terminal" : "PowerShell terminal");
        screen.ActualThemeChanged += (_, _) => screen.Background = KronoDesign.ThemeBrush(TerminalSurface, screen.ActualTheme);
        KronoInputMotion.RegisterPaletteRefresh(screen, () => screen.Background = KronoDesign.ThemeBrush(TerminalSurface, screen.ActualTheme));
        var tab = new TabViewItem { Header = name, Tag = id };
        KronoDesign.StyleTabHeader(tab, TerminalSurface);
        var terminal = new Session(tab, screen) { AgentTranscript = agent };
        screen.Loaded += (_, _) => FocusInput(terminal);
        terminals[id] = terminal;
        tabs.TabItems.Add(tab);
        tabs.SelectedItem = tab;
        return terminal;
    }

    async Task Create()
    {
        var id = Guid.NewGuid().ToString();
        var terminal = NewSession(id, "PowerShell");
        terminal.StartupInput = new();
        var screen = terminal.Screen;
        screen.BeforeTextChanging += (_, args) => {
            if (terminal.Rendering) return;
            args.Cancel = true;
            // Route keyboard, emoji, and IME text through the PTY without editing its transcript.
            var original = screen.Text;
            var start = screen.SelectionStart;
            var end = start + screen.SelectionLength;
            var suffix = original.Length - end;
            if (args.NewText.Length > start + suffix &&
                args.NewText.AsSpan(0, start).SequenceEqual(original.AsSpan(0, start)) &&
                args.NewText.AsSpan(args.NewText.Length - suffix).SequenceEqual(original.AsSpan(end))) {
                var inserted = args.NewText.Substring(start, args.NewText.Length - start - suffix)
                    .Replace("\r\n", "\r").Replace('\n', '\r');
                _ = Write(id, terminal, inserted);
            }
        };
        async Task Paste()
        {
            try {
                var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    await Write(id, terminal, (await clipboard.GetTextAsync()).Replace("\r\n", "\r").Replace('\n', '\r'));
            } catch (Exception error) { ShowError(terminal, error); }
        }
        screen.PreviewKeyDown += async (_, args) => {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if ((ctrl && args.Key == VirtualKey.V) || (shift && args.Key == VirtualKey.Insert)) {
                args.Handled = true;
                await Paste();
                return;
            }
            var input = (args.Key, ctrl, shift) switch {
                (VirtualKey.C, true, _) when screen.SelectionLength == 0 => "\u0003",
                (VirtualKey.L, true, _) => "\u000c",
                (VirtualKey.Z, true, false) => "\u001a",
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
        try {
            await worker.Request(id, "terminal:create", new { });
            if (!terminals.ContainsKey(id)) {
                await worker.Request(Guid.NewGuid().ToString(), "terminal:close", new { id });
                return;
            }
            if (terminal.StartupInput is null) return;
            var waiting = terminal.StartupInput.ToString();
            terminal.StartupInput = null;
            terminal.Ready = true;
            FocusInput(terminal);
            if (waiting.Length > 0) await Write(id, terminal, waiting);
        } catch (Exception error) {
            terminal.StartupInput = null;
            if (terminals.ContainsKey(id)) ShowError(terminal, error);
        }
    }

    async Task Write(string id, Session terminal, string input)
    {
        if (!terminals.ContainsKey(id)) return;
        if (!terminal.Ready) {
            if (terminal.StartupInput is { } waiting && input.Length <= 8192 - waiting.Length) waiting.Append(input);
            return;
        }
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
        terminal.NeedsRender = false;
        var (text, caret) = terminal.Buffer.Render();
        terminal.Rendering = true;
        try {
            terminal.Screen.Text = text;
            terminal.Screen.Select(caret, 0);
        } finally { terminal.Rendering = false; }
        ScrollOutputToEnd(terminal);
    }

    static void ScrollOutputToEnd(Session terminal)
    {
        if (terminal.ScrollQueued) return;
        terminal.ScrollQueued = terminal.Screen.DispatcherQueue.TryEnqueue(() => {
            terminal.ScrollQueued = false;
            if (!terminal.Screen.IsLoaded) return;
            var scroll = KronoInputMotion.Descendants(terminal.Screen).OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null) scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        });
    }

    public void Receive(string id, string type, JsonNode? data)
    {
        if (disposed) return;
        if (type == "terminal-agent") {
            var terminalId = "agent:" + id;
            if (!terminals.TryGetValue(terminalId, out var agent)) {
                if (data?["phase"]?.ToString() != "start") return;
                var old = terminals.FirstOrDefault(item => item.Value.AgentTranscript);
                if (terminals.Values.Count(item => item.AgentTranscript) >= 12) {
                    terminals.Remove(old.Key);
                    tabs.TabItems.Remove(old.Value.Tab);
                }
                agent = NewSession(terminalId, "Agent · PowerShell", true);
            }
            var text = data?["message"]?.ToString() ?? "";
            WriteOutput(agent, "\r\n" + text.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
            return;
        }
        if (!terminals.TryGetValue(id, out var terminal)) return;
        if (type == "terminal") {
            WriteOutput(terminal, data?.ToString() ?? "");
        } else if (type == "terminal-exit") {
            terminal.Ready = false;
            terminal.StartupInput = null;
            terminal.Screen.IsReadOnly = true;
            terminal.Tab.Header = "PowerShell · exited";
        }
    }

    public void SelectAgentRun(string id)
    {
        if (terminals.TryGetValue("agent:" + id, out var terminal)) tabs.SelectedItem = terminal.Tab;
    }

    void WriteOutput(Session terminal, string text)
    {
        terminal.Buffer.Write(text);
        terminal.NeedsRender = true;
        if (IsLoaded && ReferenceEquals(tabs.SelectedItem, terminal.Tab) && !renderTimer.IsEnabled) renderTimer.Start();
    }

    public void Dispose() { disposed = true; renderTimer.Stop(); terminals.Clear(); tabs.TabItems.Clear(); }
}
