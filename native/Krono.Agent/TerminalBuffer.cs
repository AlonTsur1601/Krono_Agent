using System.Text;

namespace Krono_Agent;

// Small VT screen model; escape sequences control a native text surface.
// No HTML, WebView or third-party terminal renderer is used.
public sealed class TerminalBuffer
{
    readonly List<StringBuilder> rows = [new()];
    readonly StringBuilder sequence = new();
    int row, column, state, savedRow, savedColumn;
    public void Write(string input)
    {
        foreach (var ch in input) {
            if (state == 3) { if (ch == '\a') state = 0; else if (ch == '\x1b') state = 4; continue; }
            if (state == 4) { state = ch == '\\' ? 0 : 3; continue; }
            if (state == 1) { if (ch == '[') { state = 2; sequence.Clear(); } else if (ch == ']') state = 3; else state = 0; continue; }
            if (state == 2) { if (ch is >= '@' and <= '~') { Command(ch, sequence.ToString()); state = 0; } else if (sequence.Length < 128) sequence.Append(ch); continue; }
            if (ch == '\x1b') { state = 1; continue; }
            if (ch == '\r') { column = 0; continue; }
            if (ch == '\n') { row++; Ensure(); continue; }
            if (ch == '\b') { column = Math.Max(0, column - 1); continue; }
            if (ch == '\t') { column = Math.Min(499, (column / 8 + 1) * 8); continue; }
            if (char.IsControl(ch)) continue;
            Ensure(); while (rows[row].Length <= column) rows[row].Append(' '); rows[row][column++] = ch;
            if (column >= 100) { column = 0; row++; Ensure(); }
        }
    }
    void Ensure()
    {
        row = Math.Clamp(row, 0, 2000);
        column = Math.Clamp(column, 0, 499);
        if (row == 2000 && rows.Count == 2000) {
            const int discarded = 500;
            rows.RemoveRange(0, discarded);
            row -= discarded;
            savedRow = Math.Max(0, savedRow - discarded);
        }
        while (rows.Count <= row) rows.Add(new StringBuilder());
    }
    void Command(char command, string args)
    {
        var values = args.TrimStart('?', '>').Split(';').Select(value => int.TryParse(value, out var number) ? number : 0).ToArray();
        var count = values.Length > 0 && values[0] > 0 ? values[0] : 1;
        switch (command) {
            case 'A': row = Math.Max(0, row - count); break;
            case 'B': row += count; break;
            case 'C': column += count; break;
            case 'D': column = Math.Max(0, column - count); break;
            case 'G': column = count - 1; break;
            case 'H': case 'f': row = count - 1; column = values.Length > 1 ? Math.Max(0, values[1] - 1) : 0; break;
            case 'J': if (values[0] is 2 or 3) { rows.Clear(); rows.Add(new StringBuilder()); row = column = 0; } else { Ensure(); rows[row].Length = Math.Min(column, rows[row].Length); if (rows.Count > row + 1) rows.RemoveRange(row + 1, rows.Count - row - 1); } break;
            case 'K': Ensure(); if (values[0] == 2) rows[row].Clear(); else if (values[0] == 0) rows[row].Length = Math.Min(column, rows[row].Length); else for (var i = 0; i <= column && i < rows[row].Length; i++) rows[row][i] = ' '; break;
            case 's': savedRow = row; savedColumn = column; break;
            case 'u': row = savedRow; column = savedColumn; break;
        }
        Ensure();
    }
    internal (string Text, int Caret) Render()
    {
        var display = new StringBuilder();
        var caret = 0;
        for (var index = 0; index < rows.Count; index++) {
            if (index > 0) display.Append('\r');
            var line = rows[index];
            var length = line.Length;
            while (length > 0 && char.IsWhiteSpace(line[length - 1])) length--;
            if (index == row) {
                caret = display.Length + column;
            }
            display.Append(line, 0, length);
            if (index == row && column > length) display.Append(' ', column - length);
            if (index == row && column >= length && line.ToString().Any(ch => ch is >= '\u0590' and <= '\u08ff' or >= '\ufb1d' and <= '\ufefc')) {
                display.Append('\u200e');
                caret++;
            }
        }
        return (display.ToString(), caret);
    }

    public override string ToString() => Render().Text;
}
