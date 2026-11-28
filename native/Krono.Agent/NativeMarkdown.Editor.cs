using System.Runtime.CompilerServices;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Krono_Agent;

public static partial class NativeMarkdown
{
    internal const string LiveEditorSetting = "native.liveMarkdown";
    static readonly ConditionalWeakTable<TextBox, LiveEditor> liveEditors = new();

    internal static FrameworkElement Editor(TextBox input, Workspace workspace, Func<bool>? pasteImage = null)
        => liveEditors.GetValue(input, field => new(field, workspace, pasteImage)).View;

    internal static void Focus(TextBox input)
    {
        if (liveEditors.TryGetValue(input, out var editor) && editor.Rich.Visibility == Visibility.Visible)
            editor.Rich.Focus(FocusState.Programmatic);
        else input.Focus(FocusState.Programmatic);
    }

    internal static void ShowSaved(TextBox input)
    {
        if (liveEditors.TryGetValue(input, out var editor)) editor.ShowSaved();
    }

    internal static void RefreshEditors(Workspace workspace)
    {
        foreach (var (_, editor) in liveEditors)
            if (ReferenceEquals(editor.Workspace, workspace) && editor.View.IsLoaded) editor.RefreshMode();
    }

    sealed class LiveEditor
    {
        readonly TextBox source;
        MarkdownDecoration[] decorations = [];
        string parsedSource = "";
        string? formattedText;
        HashSet<MarkdownDecoration> shownMarkers = [];
        bool hadDecorations;
        int? pendingSpaceCaret;
        double formattedSize;
        string formattedFont = "";
        bool syncing, showingSaved, formatQueued, selecting, composing;
        enum EditKind { Separate, Typing, Backspace, Delete }
        sealed record EditState(string Text, int Start, int End);
        readonly List<EditState> undo = [], redo = [];
        EditState? editStart;
        EditKind editKind, groupKind;
        int groupCaret = -1;
        long groupStarted, lastEdit;
        public Workspace Workspace { get; }
        public Grid View { get; } = new();
        public RichEditBox Rich { get; }

        public LiveEditor(TextBox input, Workspace workspace, Func<bool>? pasteImage)
        {
            source = input;
            Workspace = workspace;
            if (input.ReadLocalValue(TextBox.TextAlignmentProperty) == DependencyProperty.UnsetValue)
                input.TextAlignment = TextAlignment.Left;
            // Keep word-boundary autocorrection from repositioning the caret after Space.
            source.IsSpellCheckEnabled = false;
            source.IsTextPredictionEnabled = false;
            Rich = new RichEditBox {
                Header = input.Header, PlaceholderText = input.PlaceholderText,
                TextWrapping = input.TextWrapping, MinHeight = input.MinHeight, MaxHeight = input.MaxHeight,
                Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                FlowDirection = input.FlowDirection, IsEnabled = input.IsEnabled,
                TextAlignment = input.TextAlignment, TextReadingOrder = input.TextReadingOrder,
                HorizontalAlignment = input.HorizontalAlignment, VerticalAlignment = input.VerticalAlignment,
                VerticalContentAlignment = VerticalAlignment.Top,
                IsSpellCheckEnabled = false, IsTextPredictionEnabled = false,
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetAutomationId(Rich, AutomationProperties.GetAutomationId(input));
            AutomationProperties.SetName(Rich, input.Header?.ToString() ?? input.PlaceholderText);
            void Paint()
            {
                Rich.Background = KronoDesign.ThemeBrush("KronoInputFace", Rich.ActualTheme);
                Rich.BorderBrush = KronoDesign.ThemeBrush("KronoInputRim", Rich.ActualTheme);
            }
            KronoInputMotion.Attach(Rich, animatePress: input is not KronoTextBox { PressMotionEnabled: false });
            KronoInputMotion.RegisterPaletteRefresh(Rich, () => { formattedFont = ""; QueueFormat(); });
            Rich.ActualThemeChanged += (_, _) => { Paint(); formattedFont = ""; QueueFormat(); };
            Paint();
            View.Children.Add(source);
            View.Children.Add(Rich);
            View.Loaded += (_, _) => RefreshMode();
            View.Unloaded += (_, _) => { selecting = composing = false; };
            source.TextChanged += (_, _) => {
                if (syncing) return;
                undo.Clear();
                redo.Clear();
                editStart = null;
                groupCaret = -1;
                if (Rich.Visibility == Visibility.Visible) SetDocument();
            };
            source.IsEnabledChanged += (_, _) => Rich.IsEnabled = source.IsEnabled;
            Rich.TextChanged += (_, _) => {
                if (syncing || composing || Rich.Visibility != Visibility.Visible) return;
                SyncSource();
            };
            void SyncSource()
            {
                Rich.Document.GetText(TextGetOptions.UseLf, out var raw);
                var text = Normalize(raw);
                if (text != Normalize(source.Text)) {
                    RecordEdit(text);
                    syncing = true;
                    try { source.Text = text.Length <= source.MaxLength || source.MaxLength == 0 ? text : text[..source.MaxLength]; }
                    finally { syncing = false; }
                    if (Normalize(source.Text) != text) SetDocument();
                }
                QueueFormat();
            }
            // Keep native hit testing and IME composition on the same layout until the gesture commits.
            Rich.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
            {
                selecting = args.GetCurrentPoint(Rich).Properties.IsLeftButtonPressed || args.Pointer.IsInContact;
                groupCaret = -1;
                editStart = null;
            }), true);
            void FinishSelection() { selecting = false; QueueFormat(); }
            Rich.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => FinishSelection()), true);
            Rich.PointerCaptureLost += (_, _) => FinishSelection();
            Rich.PointerCanceled += (_, _) => FinishSelection();
            Rich.TextCompositionStarted += (_, _) => {
                composing = true;
                editStart = CaptureEdit();
                editKind = EditKind.Separate;
            };
            Rich.TextCompositionEnded += (_, _) => { composing = false; SyncSource(); };
            Rich.SelectionChanged += (_, _) => { if (!syncing) QueueFormat(); };
            Rich.GotFocus += (_, _) => {
                if (showingSaved) {
                    showingSaved = false;
                    // Let pointer or keyboard focus finish positioning the selection before changing editors.
                    Rich.DispatcherQueue.TryEnqueue(RefreshMode);
                } else QueueFormat();
            };
            Rich.LostFocus += (_, _) => QueueFormat();
            AddShortcut(Windows.System.VirtualKey.Z, Windows.System.VirtualKeyModifiers.Control, () => {
                RestoreEdit(undo, redo);
                return true;
            });
            AddShortcut(Windows.System.VirtualKey.Y, Windows.System.VirtualKeyModifiers.Control, () => {
                RestoreEdit(redo, undo);
                return true;
            });
            AddShortcut(Windows.System.VirtualKey.Z, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, () => {
                RestoreEdit(redo, undo);
                return true;
            });
            AddShortcut(Windows.System.VirtualKey.V, Windows.System.VirtualKeyModifiers.Control, () => {
                if (!TryReadClipboard(out var clipboard)) return pasteImage?.Invoke() == true;
                _ = PasteText(clipboard, CaptureEdit());
                return true;
            });
            void AddShortcut(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Func<bool> action)
            {
                var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers, ScopeOwner = Rich };
                shortcut.Invoked += (_, args) => {
                    if (Rich.FocusState != FocusState.Unfocused && !Rich.IsReadOnly) args.Handled = action();
                };
                Rich.KeyboardAccelerators.Add(shortcut);
            }
            Rich.PreviewKeyDown += (_, args) => {
                var control = IsKeyDown(Windows.System.VirtualKey.Control);
                if (Rich.IsReadOnly || composing) return;
                if (args.Key is Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu)
                    return;
                editStart = CaptureEdit();
                editKind = editStart.Start != editStart.End || control ? EditKind.Separate : args.Key switch {
                    Windows.System.VirtualKey.Back => EditKind.Backspace,
                    Windows.System.VirtualKey.Delete => EditKind.Delete,
                    Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or
                        Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End or
                        Windows.System.VirtualKey.Enter => EditKind.Separate,
                    _ => EditKind.Typing
                };
                if (editKind == EditKind.Separate) groupCaret = -1;
            };
            Rich.Paste += (_, args) => {
                if (!TryReadClipboard(out var clipboard)) {
                    args.Handled = pasteImage?.Invoke() == true;
                    return;
                }
                args.Handled = true;
                _ = PasteText(clipboard, CaptureEdit());
            };
            async Task PasteText(DataPackageView clipboard, EditState before)
            {
                try {
                    var text = Normalize(await clipboard.GetTextAsync()).Replace('\n', '\r');
                    if (!Rich.IsEnabled || Rich.IsReadOnly || Rich.Visibility != Visibility.Visible ||
                        Normalize(source.Text) != before.Text) return;
                    editStart = before;
                    editKind = EditKind.Separate;
                    syncing = true;
                    try {
                        Rich.Document.GetRange(before.Start, before.End).SetText(TextSetOptions.Unhide | TextSetOptions.Unlink, text);
                        var caret = before.Start + text.Length;
                        Rich.Document.Selection.SetRange(caret, caret);
                    } finally { syncing = false; }
                    SyncSource();
                } catch (System.Runtime.InteropServices.COMException) { }
            }
        }

        static bool TryReadClipboard(out DataPackageView view)
        {
            view = null!;
            try {
                view = Clipboard.GetContent();
                return view.Contains(StandardDataFormats.Text);
            } catch (System.Runtime.InteropServices.COMException) { return false; }
        }

        static bool IsKeyDown(Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        EditState CaptureEdit()
        {
            var selection = Rich.Document.Selection;
            return new(Normalize(source.Text), selection.StartPosition, selection.EndPosition);
        }

        void RecordEdit(string text)
        {
            var after = CaptureEdit();
            var before = editStart ?? after;
            var kind = editStart is null ? EditKind.Separate : editKind;
            editStart = null;
            if (before.Text == text) return;
            var now = Environment.TickCount64;
            if (kind == EditKind.Separate || before.Start != groupCaret || kind != groupKind ||
                now - lastEdit > 500 || now - groupStarted > 1000) {
                undo.Add(before);
                if (undo.Count > 100) undo.RemoveAt(0);
                groupStarted = now;
            }
            redo.Clear();
            groupKind = kind;
            groupCaret = kind is EditKind.Typing or EditKind.Backspace
                ? before.End + text.Length - before.Text.Length : before.Start;
            lastEdit = now;
        }

        void RestoreEdit(List<EditState> history, List<EditState> opposite)
        {
            if (history.Count == 0) return;
            opposite.Add(CaptureEdit());
            var step = history[^1];
            history.RemoveAt(history.Count - 1);
            editStart = null;
            groupCaret = -1;
            syncing = true;
            try { source.Text = step.Text; }
            finally { syncing = false; }
            SetDocument(step.Start, step.End);
        }

        public void ShowSaved()
        {
            if (Workspace.Setting(LiveEditorSetting) == "true" || string.IsNullOrEmpty(source.Text)) return;
            showingSaved = true;
            RefreshMode();
        }

        public void RefreshMode()
        {
            var live = Workspace.Setting(LiveEditorSetting) == "true";
            if (live) showingSaved = false;
            var enabled = live || showingSaved;
            Rich.IsReadOnly = showingSaved || source.IsReadOnly;
            if (enabled == (Rich.Visibility == Visibility.Visible)) return;
            var focus = enabled ? source.FocusState != FocusState.Unfocused : Rich.FocusState != FocusState.Unfocused;
            if (enabled) {
                var raw = source.Text;
                var start = Normalize(raw[..source.SelectionStart]).Length;
                var end = Normalize(raw[..(source.SelectionStart + source.SelectionLength)]).Length;
                Rich.Visibility = Visibility.Visible;
                source.Visibility = Visibility.Collapsed;
                SetDocument(start, end);
                if (focus && !showingSaved) Rich.Focus(FocusState.Programmatic);
            } else {
                var selection = Rich.Document.Selection;
                var start = SourceOffset(source.Text, selection.StartPosition);
                var end = SourceOffset(source.Text, selection.EndPosition);
                source.Visibility = Visibility.Visible;
                Rich.Visibility = Visibility.Collapsed;
                if (focus) source.Focus(FocusState.Programmatic);
                source.Select(start, end - start);
            }
        }

        void SetDocument(int? selectionStart = null, int? selectionEnd = null)
        {
            syncing = true;
            try {
                var text = Normalize(source.Text);
                var selection = Rich.Document.Selection;
                var start = Math.Clamp(selectionStart ?? selection.StartPosition, 0, text.Length);
                var end = Math.Clamp(selectionEnd ?? selection.EndPosition, start, text.Length);
                // RichEdit positions count one character per paragraph break, like the parser’s LF source.
                Rich.Document.SetText(TextSetOptions.Unhide | TextSetOptions.Unlink, text.Replace('\n', '\r'));
                formattedText = null;
                pendingSpaceCaret = null;
                selection.SetRange(start, end);
            } finally { syncing = false; }
            QueueFormat();
        }

        void QueueFormat()
        {
            if (!View.IsLoaded || Rich.Visibility != Visibility.Visible || formatQueued || composing || selecting) return;
            formatQueued = Rich.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => {
                formatQueued = false;
                ApplyFormat();
            });
        }

        void ApplyFormat()
        {
            if (syncing || selecting || composing || !View.IsLoaded || Rich.Visibility != Visibility.Visible) return;
            var document = Rich.Document;
            var text = Normalize(source.Text);
            var font = KronoDesign.TextFont.Source;
            var size = KronoDesign.TextSize;
            var selection = document.Selection;
            var start = selection.StartPosition;
            var end = selection.EndPosition;
            var selectionOptions = selection.Options;
            var editing = !showingSaved && Rich.FocusState != FocusState.Unfocused;
            if (text != parsedSource) {
                var previous = decorations;
                decorations = ReadDecorations(text);
                var added = text.Length - parsedSource.Length;
                var at = FindSpaceInsertion(parsedSource, text, added);
                if (at >= 0) {
                    if (formattedText == parsedSource && formattedFont == font && formattedSize == size &&
                        TryKeepSpaceFormatting(previous, at, added))
                        formattedText = text;
                    if (editing && start == end) pendingSpaceCaret = start;
                } else pendingSpaceCaret = null;
                parsedSource = text;
            }
            // Leave the visible caret with RichEdit after the key press. Repaint on the next
            // non-space edit, a deliberate selection change, or focus loss.
            if (editing && start == end && pendingSpaceCaret == start) return;
            pendingSpaceCaret = null;
            var revealed = editing
                ? decorations.Where(span => start == end
                    ? span.Start <= start && start < span.End
                    : span.Start < end && span.End > start).ToHashSet()
                : [];
            var updateStyles = text != formattedText || formattedFont != font || formattedSize != size;
            if (!updateStyles && shownMarkers.SetEquals(revealed)) return;

            syncing = true;
            document.BatchDisplayUpdates();
            try {
                var baseline = document.GetDefaultCharacterFormat();
                if (formattedFont != font || formattedSize != size) {
                    // RichEdit uses font family names. Bundled XAML font URIs include a family after '#'.
                    baseline.Name = FontName(font);
                    baseline.Size = (float)(size * .75);
                    document.SetDefaultCharacterFormat(baseline);
                    formattedSize = size;
                    formattedFont = font;
                }
                if (updateStyles && text.Length > 0 && (decorations.Length > 0 || formattedText is null || hadDecorations)) {
                    baseline.Hidden = FormatEffect.Off;
                    baseline.Bold = baseline.Italic = baseline.Strikethrough = FormatEffect.Off;
                    baseline.Underline = UnderlineType.None;
                    document.GetRange(0, text.Length).CharacterFormat = baseline;
                }
                foreach (var span in decorations) {
                    if (updateStyles) {
                        var format = document.GetRange(span.ContentStart, span.ContentEnd).CharacterFormat;
                        switch (span.Kind) {
                            case "bold": format.Bold = FormatEffect.On; break;
                            case "italic": format.Italic = FormatEffect.On; break;
                            case "bold-italic": format.Bold = format.Italic = FormatEffect.On; break;
                            case "underline": format.Underline = UnderlineType.Single; break;
                            case "strike": format.Strikethrough = FormatEffect.On; break;
                            case "code": format.Name = FontName(KronoDesign.CodeFont.Source); break;
                            case "heading":
                                format.Bold = FormatEffect.On;
                                format.Size = (float)((size + (7 - span.Level) * 2) * .75);
                                break;
                        }
                    }
                    var visible = revealed.Contains(span);
                    if (!updateStyles && visible == shownMarkers.Contains(span)) continue;
                    var hidden = visible ? FormatEffect.Off : FormatEffect.On;
                    if (span.Start < span.ContentStart)
                        document.GetRange(span.Start, span.ContentStart).CharacterFormat.Hidden = hidden;
                    if (span.ContentEnd < span.End)
                        document.GetRange(span.ContentEnd, span.End).CharacterFormat.Hidden = hidden;
                }
                shownMarkers = revealed;
                formattedText = text;
                hadDecorations = decorations.Length > 0;
            } finally {
                document.ApplyDisplayUpdates();
                if (selection.StartPosition != start || selection.EndPosition != end) selection.SetRange(start, end);
                if (start != end && selection.Options != selectionOptions) selection.Options = selectionOptions;
                if (start == end && selection.CharacterFormat.Hidden == FormatEffect.On) selection.CharacterFormat.Hidden = FormatEffect.Off;
                syncing = false;
            }
        }

        // RichEdit keeps range formatting while inserting spaces. If the Markdown did not
        // change, keep that layout so the caret retains its affinity in bidirectional text.
        static int FindSpaceInsertion(string before, string after, int added)
        {
            if (added <= 0) return -1;
            var at = 0;
            while (at < before.Length && before[at] == after[at]) at++;
            return after.AsSpan(at, added).IndexOfAnyExcept(' ') < 0 &&
                before.AsSpan(at).SequenceEqual(after.AsSpan(at + added)) ? at : -1;
        }

        bool TryKeepSpaceFormatting(MarkdownDecoration[] previous, int at, int added)
        {
            if (previous.Length != decorations.Length) return false;
            var mappedVisible = new HashSet<MarkdownDecoration>();
            for (var i = 0; i < previous.Length; i++) {
                var span = previous[i];
                if (span.Start < at && at <= span.ContentStart || span.ContentEnd <= at && at < span.End)
                    return false;
                var shifted = span with {
                    Start = span.Start >= at ? span.Start + added : span.Start,
                    ContentStart = span.ContentStart >= at ? span.ContentStart + added : span.ContentStart,
                    ContentEnd = span.ContentEnd > at ? span.ContentEnd + added : span.ContentEnd,
                    End = span.End > at ? span.End + added : span.End
                };
                if (shifted != decorations[i]) return false;
                if (shownMarkers.Contains(span)) mappedVisible.Add(shifted);
            }
            shownMarkers = mappedVisible;
            return true;
        }
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    static int SourceOffset(string source, int offset)
    {
        int index = 0;
        while (offset-- > 0 && index < source.Length) {
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n') index++;
            index++;
        }
        return index;
    }
    static string FontName(string source) => source[(source.LastIndexOf('#') + 1)..];

    sealed record MarkdownDecoration(int Start, int ContentStart, int ContentEnd, int End, string Kind, int Level = 0);

    static MarkdownDecoration[] ReadDecorations(string text)
    {
        var spans = new List<MarkdownDecoration>();
        void Add(int start, int contentStart, int contentEnd, int end, string kind, int level = 0)
        {
            if (start >= 0 && start <= contentStart && contentStart < contentEnd && contentEnd <= end && end <= text.Length)
                spans.Add(new(start, contentStart, contentEnd, end, kind, level));
        }

        void ReadInlines(ContainerInline container)
        {
            var underlines = new Stack<HtmlInline>();
            foreach (var inline in container) {
                var start = inline.Span.Start;
                var end = inline.Span.End + 1;
                switch (inline) {
                    case EmphasisInline emphasis:
                        var count = emphasis.DelimiterCount;
                        var kind = emphasis.DelimiterChar switch {
                            '+' => "underline", '~' => "strike",
                            _ => count switch { 1 => "italic", 2 => "bold", _ => "bold-italic" }
                        };
                        Add(start, start + count, end - count, end, kind);
                        break;
                    case CodeInline code:
                        Add(start, start + code.DelimiterCount, end - code.DelimiterCount, end, "code");
                        break;
                    case LinkInline { IsImage: false, FirstChild: { } first, LastChild: { } last }:
                        Add(start, first.Span.Start, last.Span.End + 1, end, "underline");
                        break;
                    case HtmlInline html when html.Tag.Equals("<u>", StringComparison.OrdinalIgnoreCase):
                        underlines.Push(html);
                        break;
                    case HtmlInline html when html.Tag.Equals("</u>", StringComparison.OrdinalIgnoreCase) && underlines.TryPop(out var open):
                        Add(open.Span.Start, open.Span.End + 1, start, end, "underline");
                        break;
                }
                if (inline is ContainerInline nested) ReadInlines(nested);
            }
        }

        void ReadBlocks(ContainerBlock blocks)
        {
            foreach (var block in blocks) {
                if (block is HeadingBlock { Inline: { FirstChild: { } first, LastChild: { } last } } heading)
                    Add(heading.Span.Start, first.Span.Start, last.Span.End + 1, heading.Span.End + 1, "heading", heading.Level);
                if (block is FencedCodeBlock fence) {
                    var start = fence.Span.Start;
                    var end = Math.Min(text.Length, fence.Span.End + 1);
                    if (start >= 0 && end > start) {
                        var firstLine = text.IndexOf('\n', start, end - start);
                        var lastLine = text.LastIndexOf('\n', end - 1, end - start);
                        if (firstLine >= 0 && lastLine > firstLine && text.AsSpan(lastLine + 1, end - lastLine - 1).TrimStart().StartsWith(new string(fence.FencedChar, 3)))
                            Add(start, firstLine + 1, lastLine, end, "code");
                    }
                }
                if (block is LeafBlock { Inline: { } inlines }) ReadInlines(inlines);
                else if (block is ContainerBlock nested) ReadBlocks(nested);
            }
        }
        ReadBlocks(Markdig.Markdown.Parse(text, Pipeline));
        return spans.ToArray();
    }
}
