using System.Text;

namespace Krono_Agent;

public sealed record TextAttachment(string Name, string Text, int Bytes)
{
    public const int MaxBytes = 64 * 1024;
    public static readonly string[] Extensions = [".txt", ".md", ".markdown", ".csv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".toml", ".log", ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".ps1", ".sh", ".sql", ".html", ".css", ".svg", ".java", ".go", ".rs", ".cpp", ".c", ".h"];
    public static async Task<TextAttachment> Read(string path)
    {
        var name = Path.GetFileName(path);
        if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a text or source file.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxBytes) throw new InvalidOperationException("Each attached text file must be 64 KB or smaller.");
        var bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes);
        string text;
        try {
            text = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2)
                : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2)
                : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff');
        } catch (DecoderFallbackException) { throw new InvalidOperationException("The file is not valid UTF-8 or UTF-16 text."); }
        if (text.Contains('\0')) throw new InvalidOperationException("This file contains binary data.");
        return new(name, text, bytes.Length);
    }
    public static string Compose(string prompt, IReadOnlyList<TextAttachment> attachments)
    {
        if (attachments.Count == 0) return prompt;
        var result = new StringBuilder(string.IsNullOrWhiteSpace(prompt) ? "Review the attached files." : prompt);
        foreach (var attachment in attachments) {
            static int Run(string text, char marker) { var max = 0; var current = 0; foreach (var ch in text) { current = ch == marker ? current + 1 : 0; max = Math.Max(max, current); } return max; }
            var ticks = Run(attachment.Text, '`'); var waves = Run(attachment.Text, '~');
            var fence = new string(ticks <= waves ? '`' : '~', Math.Max(3, Math.Min(ticks, waves) + 1));
            result.Append("\n\n**Attached text: `").Append(attachment.Name.Replace('`', '′')).Append("`**\n\n").Append(fence).Append('\n').Append(attachment.Text).Append('\n').Append(fence);
        }
        return result.ToString();
    }
}
