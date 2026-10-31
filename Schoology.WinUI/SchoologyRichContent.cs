using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Schoology.WinUI;

/// <summary>
/// Converts Schoology rich-text fragments into selectable native WinUI text.
/// Only common text-formatting tags and safe HTTP(S) links are interpreted.
/// </summary>
internal static class SchoologyRichContent
{
    private static readonly Regex Tokens = new(@"(<[^>]*>|[^<]+)", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"^<\s*(/?)\s*([a-zA-Z0-9]+)([^>]*)>$", RegexOptions.Compiled);
    private static readonly Regex Href = new(@"\bhref\s*=\s*(['""])(.*?)\1", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static RichTextBlock Create(string? markup, double fontSize, Func<Uri, Task>? openLink = null)
    {
        var block = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = fontSize
        };
        if (string.IsNullOrWhiteSpace(markup)) return block;

        var paragraph = new Paragraph { Margin = new Thickness(0) };
        block.Blocks.Add(paragraph);
        var bold = false;
        var italic = false;
        var underline = false;
        var skipping = false;
        Hyperlink? activeLink = null;

        void NewParagraph()
        {
            if (paragraph.Inlines.Count == 0) return;
            paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            block.Blocks.Add(paragraph);
            activeLink = null;
        }

        void AddRun(string value)
        {
            if (value.Length == 0) return;
            var run = new Run
            {
                Text = WebUtility.HtmlDecode(value),
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
                TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None
            };
            if (activeLink is not null) activeLink.Inlines.Add(run);
            else paragraph.Inlines.Add(run);
        }

        foreach (Match match in Tokens.Matches(markup))
        {
            var token = match.Value;
            if (!token.StartsWith('<'))
            {
                if (!skipping) AddRun(token);
                continue;
            }

            var parsed = Tag.Match(token);
            if (!parsed.Success) continue;
            var closing = parsed.Groups[1].Value == "/";
            var name = parsed.Groups[2].Value.ToLowerInvariant();
            var attributes = parsed.Groups[3].Value;

            if (name is "script" or "style" or "iframe" or "object" or "svg")
            {
                skipping = !closing;
                continue;
            }
            if (skipping) continue;
            if (name is "p" or "div" or "blockquote" or "ul" or "ol")
            {
                if (closing) NewParagraph();
                else if (paragraph.Inlines.Count > 0) NewParagraph();
                continue;
            }
            if (name == "br")
            {
                paragraph.Inlines.Add(new LineBreak());
                continue;
            }
            if (name == "li")
            {
                if (closing) NewParagraph();
                else
                {
                    if (paragraph.Inlines.Count > 0) NewParagraph();
                    AddRun("• ");
                }
                continue;
            }
            if (name is "strong" or "b") { bold = !closing; continue; }
            if (name is "em" or "i") { italic = !closing; continue; }
            if (name == "u") { underline = !closing; continue; }
            if (name == "a")
            {
                if (closing) { activeLink = null; continue; }
                var hrefMatch = Href.Match(attributes);
                if (!hrefMatch.Success) continue;
                var href = WebUtility.HtmlDecode(hrefMatch.Groups[2].Value);
                if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                    !Uri.TryCreate(new Uri("https://app.schoology.com/"), href, out uri)) continue;
                if (uri is null || uri.Scheme is not ("http" or "https")) continue;
                activeLink = new Hyperlink();
                if (openLink is not null)
                    activeLink.Click += async (_, _) => await openLink(uri);
                paragraph.Inlines.Add(activeLink);
            }
        }

        return block;
    }
}
