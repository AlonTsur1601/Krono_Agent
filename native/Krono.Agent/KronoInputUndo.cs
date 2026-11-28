using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Krono_Agent;

internal sealed class KronoInputUndo
{
    internal sealed record State(string Text, int Start, int End);
    enum Kind { Separate, Typing, Backspace, Delete }
    readonly List<State> undo = [], redo = [];
    static readonly ConditionalWeakTable<TextBox, KronoInputUndo> attached = new();
    State? beforeEdit;
    Kind editKind, groupKind;
    int groupCaret = -1;
    long groupStarted, lastEdit;

    internal void BreakGroup() { beforeEdit = null; groupCaret = -1; }
    internal void Clear() { undo.Clear(); redo.Clear(); BreakGroup(); }
    internal void Separate(State state) { beforeEdit = state; editKind = Kind.Separate; }
    internal void PrepareIfNone(State state) { if (beforeEdit is null) Separate(state); }

    internal void PrepareKey(State state, VirtualKey key)
    {
        if (key is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu) return;
        var control = KeyDown(VirtualKey.Control);
        beforeEdit = state;
        editKind = state.Start != state.End || control ? Kind.Separate : key switch {
            VirtualKey.Back => Kind.Backspace,
            VirtualKey.Delete => Kind.Delete,
            VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
                VirtualKey.Home or VirtualKey.End or VirtualKey.Enter => Kind.Separate,
            _ => Kind.Typing
        };
        if (editKind == Kind.Separate) groupCaret = -1;
    }

    internal void Record(State fallback, string text)
    {
        var before = beforeEdit ?? fallback;
        var kind = beforeEdit is null ? Kind.Separate : editKind;
        beforeEdit = null;
        if (before.Text == text) return;
        var now = Environment.TickCount64;
        if (kind == Kind.Separate || before.Start != groupCaret || kind != groupKind ||
            now - lastEdit > 500 || now - groupStarted > 1000) {
            undo.Add(before);
            if (undo.Count > 100) undo.RemoveAt(0);
            groupStarted = now;
        }
        redo.Clear();
        groupKind = kind;
        groupCaret = kind is Kind.Typing or Kind.Backspace
            ? before.End + text.Length - before.Text.Length : before.Start;
        lastEdit = now;
    }

    internal void BindShortcuts(Control field, Func<bool> canEdit, Func<State> capture, Action<State> restore)
    {
        Bind(field, VirtualKey.Z, VirtualKeyModifiers.Control, canEdit, () => Move(undo, redo));
        Bind(field, VirtualKey.Y, VirtualKeyModifiers.Control, canEdit, () => Move(redo, undo));
        Bind(field, VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, canEdit, () => Move(redo, undo));
        bool Move(List<State> history, List<State> opposite)
        {
            if (history.Count == 0) return true;
            opposite.Add(capture());
            var step = history[^1];
            history.RemoveAt(history.Count - 1);
            BreakGroup();
            restore(step);
            return true;
        }
    }

    internal static void Bind(Control field, VirtualKey key, VirtualKeyModifiers modifiers, Func<bool> canEdit, Func<bool> action)
    {
        field.PreviewKeyDown += (_, args) => {
            if (args.Key != key || !KeyDown(VirtualKey.Control) || KeyDown(VirtualKey.Menu) ||
                KeyDown(VirtualKey.Shift) != modifiers.HasFlag(VirtualKeyModifiers.Shift) || !canEdit()) return;
            args.Handled = action();
        };
    }

    internal static bool KeyDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    internal static void Attach(TextBox field, bool sensitive = false)
    {
        if (attached.TryGetValue(field, out _)) return;
        var history = new KronoInputUndo();
        attached.Add(field, history);
        var previous = field.Text;
        var restoring = false;
        State Capture() => new(field.Text, field.SelectionStart, field.SelectionStart + field.SelectionLength);
        field.PreviewKeyDown += (_, args) => history.PrepareKey(Capture(), args.Key);
        field.Paste += (_, _) => history.Separate(Capture());
        field.PointerPressed += (_, _) => history.BreakGroup();
        field.LostFocus += (_, _) => { if (sensitive) history.Clear(); else history.BreakGroup(); };
        field.BeforeTextChanging += (_, _) => {
            if (!restoring && field.FocusState != FocusState.Unfocused)
                history.PrepareIfNone(new(previous, field.SelectionStart, field.SelectionStart + field.SelectionLength));
        };
        field.TextChanging += (_, _) => {
            if (!restoring && field.Text != previous) {
                if (field.FocusState != FocusState.Unfocused)
                    history.Record(new(previous, field.SelectionStart, field.SelectionStart + field.SelectionLength), field.Text);
                else history.Clear();
            }
            previous = field.Text;
        };
        history.BindShortcuts(field, () => !field.IsReadOnly, Capture, step => {
            restoring = true;
            try {
                field.Text = step.Text;
                var start = Math.Clamp(step.Start, 0, step.Text.Length);
                var end = Math.Clamp(step.End, start, step.Text.Length);
                field.Select(start, end - start);
            } finally { restoring = false; previous = field.Text; }
        });
        Bind(field, VirtualKey.V, VirtualKeyModifiers.Control, () => !field.IsReadOnly, () => {
            if (!TryReadClipboard(out var clipboard)) return false;
            _ = PastePlainText(clipboard, Capture());
            return true;
        });
        async Task PastePlainText(DataPackageView clipboard, State before)
        {
            try {
                var text = await clipboard.GetTextAsync();
                if (!field.IsEnabled || field.IsReadOnly || field.FocusState == FocusState.Unfocused || field.Text != before.Text) return;
                restoring = true;
                try {
                    field.Select(before.Start, before.End - before.Start);
                    field.SelectedText = text;
                    history.Separate(before);
                    history.Record(before, field.Text);
                } finally { restoring = false; previous = field.Text; }
            } catch (System.Runtime.InteropServices.COMException) { }
        }
    }

    internal static void Attach(PasswordBox field)
    {
        var history = new KronoInputUndo();
        var previous = field.Password;
        var restoring = false;
        State Capture() => new(field.Password, field.Password.Length, field.Password.Length);
        field.PreviewKeyDown += (_, args) => history.PrepareKey(Capture(), args.Key);
        field.Paste += (_, _) => history.Separate(Capture());
        field.PointerPressed += (_, _) => history.BreakGroup();
        field.LostFocus += (_, _) => history.Clear();
        field.PasswordChanged += (_, _) => {
            if (!restoring) {
                if (field.FocusState != FocusState.Unfocused) history.Record(new(previous, previous.Length, previous.Length), field.Password);
                else history.Clear();
            }
            previous = field.Password;
        };
        history.BindShortcuts(field, () => field.IsEnabled, Capture, step => {
            restoring = true;
            try { field.Password = step.Text; }
            finally { restoring = false; }
        });
    }

    internal static bool TryReadClipboard(out DataPackageView view)
    {
        view = null!;
        try {
            view = Clipboard.GetContent();
            return view.Contains(StandardDataFormats.Text);
        } catch (System.Runtime.InteropServices.COMException) { return false; }
    }
}
