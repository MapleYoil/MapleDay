using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Services;

public static partial class MarkdownText
{
    public static RichTextBlock Create(string markdown, double fontSize = 14)
    {
        var view = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = Math.Max(22, fontSize * 1.55)
        };
        Set(view, markdown);
        return view;
    }

    public static void Set(RichTextBlock view, string markdown)
    {
        view.Blocks.Clear();
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            var paragraph = new Paragraph();
            var content = line;
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                paragraph.FontSize = 15;
                paragraph.FontWeight = FontWeights.SemiBold;
                content = line[4..];
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                paragraph.FontSize = 16;
                paragraph.FontWeight = FontWeights.SemiBold;
                content = line[3..];
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                paragraph.FontSize = 18;
                paragraph.FontWeight = FontWeights.SemiBold;
                content = line[2..];
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run { Text = "• " });
                content = line[2..];
            }
            else if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run { Text = "• " });
                content = line[2..];
            }
            AddInline(paragraph, content);
            view.Blocks.Add(paragraph);
        }
    }

    private static void AddInline(Paragraph paragraph, string content)
    {
        var position = 0;
        foreach (Match match in InlinePattern().Matches(content))
        {
            if (match.Index > position) paragraph.Inlines.Add(new Run { Text = content[position..match.Index] });
            var value = match.Value;
            if (value.StartsWith("**", StringComparison.Ordinal))
                paragraph.Inlines.Add(new Bold { Inlines = { new Run { Text = value[2..^2] } } });
            else if (value.StartsWith('`'))
                paragraph.Inlines.Add(new Run { Text = value[1..^1], FontFamily = new FontFamily("Consolas") });
            else
            {
                var closing = value.IndexOf("](", StringComparison.Ordinal);
                var uri = value[(closing + 2)..^1];
                if (Uri.TryCreate(uri, UriKind.Absolute, out var target) && target.Scheme == Uri.UriSchemeHttps)
                    paragraph.Inlines.Add(new Hyperlink { NavigateUri = target, Inlines = { new Run { Text = value[1..closing] } } });
                else paragraph.Inlines.Add(new Run { Text = value });
            }
            position = match.Index + match.Length;
        }
        if (position < content.Length) paragraph.Inlines.Add(new Run { Text = content[position..] });
    }

    [GeneratedRegex(@"\*\*[^*\r\n]+\*\*|`[^`\r\n]+`|\[[^\]\r\n]+\]\(https://[^\s)]+\)", RegexOptions.CultureInvariant)]
    private static partial Regex InlinePattern();
}
