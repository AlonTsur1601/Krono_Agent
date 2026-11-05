using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Krono_Agent;

public static class NativeMarkdown
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    public static RichTextBlock Render(string source, string textFont, double textSize, string codeFont, double codeSize, string palette = "classic")
    {
        var resolvedCodeFont = KronoDesign.ResolveFont(codeFont);
        var output = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontFamily = KronoDesign.ResolveFont(textFont), FontSize = textSize, LineHeight = textSize * 1.7 };
        void AddBlocks(ContainerBlock blocks, int depth = 0)
        {
            foreach (var block in blocks) {
                if (block is CodeBlock code) {
                    var paragraph = new Paragraph { Margin = new Thickness(depth * 18, 8, 0, 12) };
                    var content = code.Lines.ToString();
                    var tokenPattern = "(?<comment>//[^\\n]*|#[^\\n]*)|(?<literal>\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*')|(?<keyword>\\b(?:const|let|var|function|class|return|if|else|for|while|import|from|export|public|private|async|await|def|true|false|null|None|new|using|void|int|string)\\b)|(?<number>\\b\\d+(?:\\.\\d+)?\\b)";
                    var position = 0;
                    foreach (Match token in Regex.Matches(content, tokenPattern)) {
                        if (token.Index > position) paragraph.Inlines.Add(new Run { Text = content[position..token.Index], FontFamily = resolvedCodeFont, FontSize = codeSize });
                        var color = token.Groups["comment"].Success ? Windows.UI.Color.FromArgb(255, 126, 148, 137) : token.Groups["literal"].Success ? Windows.UI.Color.FromArgb(255, 143, 215, 199) : token.Groups["number"].Success ? Windows.UI.Color.FromArgb(255, 225, 169, 112) : palette == "halloween" ? Windows.UI.Color.FromArgb(255, 245, 158, 80) : palette == "mint" ? Windows.UI.Color.FromArgb(255, 79, 183, 159) : Windows.UI.Color.FromArgb(255, 161, 138, 215);
                        paragraph.Inlines.Add(new Run { Text = token.Value, FontFamily = resolvedCodeFont, FontSize = codeSize, Foreground = new SolidColorBrush(color) });
                        position = token.Index + token.Length;
                    }
                    if (position < content.Length) paragraph.Inlines.Add(new Run { Text = content[position..], FontFamily = resolvedCodeFont, FontSize = codeSize });
                    output.Blocks.Add(paragraph);
                } else if (block is LeafBlock leaf) {
                    var paragraph = new Paragraph { Margin = new Thickness(depth * 18, 0, 0, 8) };
                    if (block.Parent is ListItemBlock) paragraph.Inlines.Add(new Run { Text = "• " });
                    if (leaf.Inline is not null) AddInlines(leaf.Inline, paragraph.Inlines);
                    else paragraph.Inlines.Add(new Run { Text = leaf.Lines.ToString() });
                    if (block is HeadingBlock heading) { paragraph.FontSize = textSize + (7 - heading.Level) * 2; paragraph.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
                    output.Blocks.Add(paragraph);
                } else if (block is ThematicBreakBlock) {
                    var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run { Text = "────────────────────────" }); output.Blocks.Add(paragraph);
                } else if (block is ContainerBlock container) AddBlocks(container, depth + (block is ListBlock ? 1 : 0));
            }
        }
        void AddInlines(ContainerInline container, InlineCollection target)
        {
            foreach (var inline in container) {
                switch (inline) {
                    case LiteralInline literal: target.Add(new Run { Text = literal.Content.ToString() }); break;
                    case CodeInline code: target.Add(new Run { Text = code.Content, FontFamily = resolvedCodeFont, FontSize = codeSize }); break;
                    case LineBreakInline: target.Add(new LineBreak()); break;
                    case LinkInline link when !link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http":
                        var hyperlink = new Hyperlink(); AddInlines(link, hyperlink.Inlines); hyperlink.Click += (_, _) => Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); target.Add(hyperlink); break;
                    case EmphasisInline emphasis:
                        Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic(); AddInlines(emphasis, span.Inlines); target.Add(span); break;
                    case ContainerInline nested: AddInlines(nested, target); break;
                    default: target.Add(new Run { Text = inline.ToString() ?? "" }); break;
                }
            }
        }
        AddBlocks(Markdig.Markdown.Parse(source, Pipeline)); return output;
    }
}
