using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Schoology.WinUI;

/// <summary>
/// Converts Schoology HTML fragments into selectable native WinUI rich text.
/// Keeps inline HTTP(S) links clickable and preserves nested formatting and font
/// family/size metadata without allowing active or embedded content.
/// </summary>
internal static class SchoologyRichContent
{
    private static readonly Regex Tokens = new(@"<!--[\s\S]*?-->|<![^>]*>|<[^>]*>|[^<]+|<",
        RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"^<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9:-]*)\b([^>]*)>$",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Attribute = new(
        @"(?<name>[a-zA-Z_:][\w:.-]*)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Uri SchoologyBaseUri = new("https://app.schoology.com/");

    private static Windows.UI.Color EnsureDarkThemeContrast(Windows.UI.Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Windows.UI.Color value) =>
            0.2126 * Linear(value.R) + 0.7152 * Linear(value.G) + 0.0722 * Linear(value.B);
        static double Contrast(double first, double second) =>
            (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

        // Schoology post cards use a dark neutral surface in Dark theme.
        const double backgroundLuminance = 0.014;
        if (Contrast(Luminance(color), backgroundLuminance) >= 4.5) return color;

        for (var step = 1; step <= 20; step++)
        {
            var amount = step / 20d;
            var adjusted = Windows.UI.Color.FromArgb(color.A,
                (byte)Math.Round(color.R + (255 - color.R) * amount),
                (byte)Math.Round(color.G + (255 - color.G) * amount),
                (byte)Math.Round(color.B + (255 - color.B) * amount));
            if (Contrast(Luminance(adjusted), backgroundLuminance) >= 4.5)
                return adjusted;
        }
        return Windows.UI.Color.FromArgb(color.A, 255, 255, 255);
    }

    private readonly record struct InlineStyle(
        string? FontFamily, double FontSize, FontWeight FontWeight, FontStyle FontStyle,
        bool Underline, Windows.UI.Color? ForegroundColor);

    private readonly record struct StyleFrame(string Tag, InlineStyle PreviousStyle, Uri? PreviousLink);

    internal static RichTextBlock Create(string? markup, double fontSize,
        Func<Uri, Task>? openLink = null, bool darkTheme = false, double minimumFontSize = 15.5)
    {
        // Match SwiftUI's 15.5pt minimum @ScaledMetric, while retaining the
        // user's Windows text-scale preference for accessibility.
        minimumFontSize = Math.Clamp(minimumFontSize, 12d, 32d);
        var effectiveFontSize = Math.Max(fontSize, minimumFontSize);
        var block = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            IsTextScaleFactorEnabled = true,
            FontSize = effectiveFontSize
        };
        if (string.IsNullOrWhiteSpace(markup)) return block;

        // Some Schoology responses return the entire rich-text fragment HTML-encoded.
        var markupWasEncoded = !markup.Contains('<') && markup.Contains("&lt;", StringComparison.OrdinalIgnoreCase);
        if (markupWasEncoded) markup = WebUtility.HtmlDecode(markup);

        var paragraph = new Paragraph { Margin = new Thickness(0) };
        block.Blocks.Add(paragraph);
        var style = new InlineStyle(null, effectiveFontSize, FontWeights.Normal, FontStyle.Normal, false, null);
        var frames = new Stack<StyleFrame>();
        Uri? activeUri = null;
        Hyperlink? activeLink = null;
        string? skippedTag = null;
        var skippedDepth = 0;

        void EnsureLink()
        {
            if (activeUri is null || activeLink is not null) return;
            var uri = activeUri;
            activeLink = new Hyperlink();
            if (openLink is not null)
                activeLink.Click += async (_, _) => await openLink(uri);
            paragraph.Inlines.Add(activeLink);
        }

        void NewParagraph(double leftIndent = 0)
        {
            if (paragraph.Inlines.Count == 0) return;
            paragraph = new Paragraph
            {
                Margin = new Thickness(leftIndent, 2, 0, 2)
            };
            block.Blocks.Add(paragraph);
            activeLink = null;
        }

        void AddRun(string value)
        {
            if (value.Length == 0) return;
            var run = new Run
            {
                Text = markupWasEncoded ? value : WebUtility.HtmlDecode(value),
                FontSize = Math.Max(style.FontSize, minimumFontSize),
                FontWeight = style.FontWeight,
                FontStyle = style.FontStyle
            };
            if (!string.IsNullOrWhiteSpace(style.FontFamily))
                run.FontFamily = new FontFamily(style.FontFamily);
            if (style.ForegroundColor is { } foregroundColor)
                run.Foreground = new SolidColorBrush(darkTheme
                    ? EnsureDarkThemeContrast(foregroundColor) : foregroundColor);

            Inline inline = run;
            if (style.Underline)
            {
                var underline = new Underline();
                underline.Inlines.Add(inline);
                inline = underline;
            }

            if (activeUri is not null)
            {
                EnsureLink();
                activeLink!.Inlines.Add(inline);
            }
            else paragraph.Inlines.Add(inline);
        }

        static bool IsVoidTag(string name) =>
            name is "br" or "img" or "hr" or "meta" or "link" or "input" or "source" or "wbr";

        string? AttributeValue(string attributes, string name)
        {
            foreach (Match match in Attribute.Matches(attributes))
            {
                if (!match.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var value = match.Groups["double"].Success ? match.Groups["double"].Value :
                    match.Groups["single"].Success ? match.Groups["single"].Value :
                    match.Groups["bare"].Value;
                return markupWasEncoded ? value : WebUtility.HtmlDecode(value);
            }
            return null;
        }

        static string? NormalizeFontFamily(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var family = value.Split(',')[0].Trim().Trim('"', '\'');
            if (family.Length == 0 || family.Length > 80 ||
                family.Any(character => char.IsControl(character) || character is '<' or '>' or ';' or '{' or '}'))
                return null;
            return family.ToLowerInvariant() switch
            {
                "sans-serif" or "system-ui" => "Segoe UI",
                "serif" => "Georgia",
                "monospace" => "Consolas",
                _ => family
            };
        }

        static double ParseFontSize(string? value, double current)
        {
            if (string.IsNullOrWhiteSpace(value)) return current;
            var text = value.Trim().ToLowerInvariant();
            if (double.TryParse(text.TrimEnd('p', 'x', 't', 'e', 'm', '%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                if (text.EndsWith("pt", StringComparison.Ordinal)) number *= 96d / 72d;
                else if (text.EndsWith("em", StringComparison.Ordinal)) number *= current;
                else if (text.EndsWith('%')) number = current * number / 100d;
                else if (text.EndsWith("px", StringComparison.Ordinal)) number *= 0.75d;
                return Math.Clamp(number, 8d, 48d);
            }
            return current;
        }

        static bool TryParseColor(string? value, out Windows.UI.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim().ToLowerInvariant();
            var named = text switch
            {
                "black" => "#000000", "white" => "#ffffff", "red" => "#ff0000",
                "green" => "#008000", "blue" => "#0000ff", "yellow" => "#ffff00",
                "orange" => "#ffa500", "purple" => "#800080", "gray" or "grey" => "#808080",
                "silver" => "#c0c0c0", "maroon" => "#800000", "navy" => "#000080",
                "teal" => "#008080", "olive" => "#808000", "lime" => "#00ff00",
                "aqua" or "cyan" => "#00ffff", "fuchsia" or "magenta" => "#ff00ff",
                "transparent" => "#00000000", _ => text
            };
            if (named.StartsWith('#'))
            {
                var hex = named[1..];
                if (hex.Length == 3)
                    hex = string.Concat(hex.Select(character => new string(character, 2)));
                if (hex.Length == 6) hex = "ff" + hex;
                if (hex.Length == 8 &&
                    uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var argb))
                {
                    color = Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16),
                        (byte)(argb >> 8), (byte)argb);
                    return true;
                }
                return false;
            }

            var rgb = Regex.Match(text,
                @"^rgba?\(\s*(?<r>[\d.]+)\s*,\s*(?<g>[\d.]+)\s*,\s*(?<b>[\d.]+)(?:\s*,\s*(?<a>[\d.]+)\s*)?\)$",
                RegexOptions.CultureInvariant);
            if (!rgb.Success ||
                !byte.TryParse(rgb.Groups["r"].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var red) ||
                !byte.TryParse(rgb.Groups["g"].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var green) ||
                !byte.TryParse(rgb.Groups["b"].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var blue))
                return false;
            byte alpha = 255;
            if (rgb.Groups["a"].Success &&
                double.TryParse(rgb.Groups["a"].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var opacity))
                alpha = (byte)Math.Clamp(Math.Round(opacity <= 1 ? opacity * 255 : opacity), 0, 255);
            color = Windows.UI.Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        static InlineStyle ApplyCss(InlineStyle current, string? css)
        {
            if (string.IsNullOrWhiteSpace(css)) return current;
            foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0) continue;
                var property = declaration[..separator].Trim().ToLowerInvariant();
                var value = declaration[(separator + 1)..].Trim();
                current = property switch
                {
                    "font-family" => current with { FontFamily = NormalizeFontFamily(value) ?? current.FontFamily },
                    "font-size" => current with { FontSize = ParseFontSize(value, current.FontSize) },
                    "color" => TryParseColor(value, out var parsedColor)
                        ? current with { ForegroundColor = parsedColor } : current,
                    "font-weight" => current with
                    {
                        FontWeight = value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("bolder", StringComparison.OrdinalIgnoreCase) ||
                            (int.TryParse(value, out var weight) && weight >= 600)
                                ? FontWeights.Bold : FontWeights.Normal
                    },
                    "font-style" => current with
                    {
                        FontStyle = value.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("oblique", StringComparison.OrdinalIgnoreCase)
                                ? FontStyle.Italic : FontStyle.Normal
                    },
                    "text-decoration" or "text-decoration-line" => current with
                    {
                        Underline = value.Contains("underline", StringComparison.OrdinalIgnoreCase)
                    },
                    _ => current
                };
            }
            return current;
        }

        InlineStyle ApplyTag(string name, string attributes, InlineStyle current)
        {
            switch (name)
            {
                case "b":
                case "strong":
                    return current with { FontWeight = FontWeights.Bold };
                case "i":
                case "em":
                case "cite":
                    return current with { FontStyle = FontStyle.Italic };
                case "u":
                    return current with { Underline = true };
                case "small":
                    return current with { FontSize = Math.Clamp(current.FontSize * 0.85d, 8d, 48d) };
                case "big":
                    return current with { FontSize = Math.Clamp(current.FontSize * 1.15d, 8d, 48d) };
                case "code":
                case "pre":
                case "tt":
                    return current with { FontFamily = "Consolas" };
                case "h1":
                    return current with { FontWeight = FontWeights.Bold, FontSize = Math.Clamp(current.FontSize * 1.6d, 8d, 48d) };
                case "h2":
                    return current with { FontWeight = FontWeights.Bold, FontSize = Math.Clamp(current.FontSize * 1.4d, 8d, 48d) };
                case "h3":
                    return current with { FontWeight = FontWeights.Bold, FontSize = Math.Clamp(current.FontSize * 1.25d, 8d, 48d) };
                case "h4":
                case "h5":
                case "h6":
                    return current with { FontWeight = FontWeights.SemiBold };
                case "font":
                    var face = NormalizeFontFamily(AttributeValue(attributes, "face"));
                    var size = AttributeValue(attributes, "size");
                    var colorValue = AttributeValue(attributes, "color");
                    var fontStyle = TryParseColor(colorValue, out var fontColor)
                        ? current with { ForegroundColor = fontColor } : current;
                    if (int.TryParse(size, out var htmlSize))
                    {
                        var scale = htmlSize switch
                        {
                            <= 1 => 0.75d, 2 => 0.875d, 3 => 1d, 4 => 1.125d,
                            5 => 1.25d, 6 => 1.5d, _ => 1.75d
                        };
                        return fontStyle with
                        {
                            FontFamily = face ?? current.FontFamily,
                            FontSize = Math.Clamp(current.FontSize * scale, 8d, 48d)
                        };
                    }
                    return fontStyle with { FontFamily = face ?? current.FontFamily };
                case "span":
                    return ApplyCss(current, AttributeValue(attributes, "style"));
                default:
                    return current;
            }
        }

        static bool IsBlockTag(string name) =>
            name is "p" or "div" or "section" or "article" or "blockquote" or "ul" or "ol" or
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre";

        static bool IsFormattingTag(string name) =>
            name is "b" or "strong" or "i" or "em" or "cite" or "u" or "small" or "big" or
                "code" or "pre" or "tt" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
                "font" or "span";

        void RestoreTag(string name)
        {
            if (!frames.Any(frame => frame.Tag == name)) return;
            while (frames.Count > 0)
            {
                var frame = frames.Pop();
                style = frame.PreviousStyle;
                activeUri = frame.PreviousLink;
                if (frame.Tag == name) break;
            }
            activeLink = null;
            if (activeUri is not null) EnsureLink();
        }

        foreach (Match match in Tokens.Matches(markup))
        {
            var token = match.Value;
            if (token.StartsWith("<!--", StringComparison.Ordinal) || token.StartsWith("<!", StringComparison.Ordinal))
                continue;
            if (!token.StartsWith('<'))
            {
                if (skippedDepth == 0) AddRun(token);
                continue;
            }

            var parsed = Tag.Match(token);
            if (!parsed.Success) continue;
            var closing = parsed.Groups[1].Value == "/";
            var name = parsed.Groups[2].Value.ToLowerInvariant();
            var attributes = parsed.Groups[3].Value;
            var selfClosing = attributes.TrimEnd().EndsWith('/') || IsVoidTag(name);

            if (skippedDepth > 0)
            {
                if (name == skippedTag && !selfClosing)
                {
                    skippedDepth += closing ? -1 : 1;
                    if (skippedDepth == 0) skippedTag = null;
                }
                continue;
            }
            if (name is "script" or "style" or "iframe" or "object" or "svg" or "video" or "audio")
            {
                if (!closing && !selfClosing) { skippedTag = name; skippedDepth = 1; }
                continue;
            }

            if (closing)
            {
                if (IsFormattingTag(name) || name == "a" || IsBlockTag(name)) RestoreTag(name);
                if (name is "p" or "div" or "section" or "article" or "blockquote" or
                    "ul" or "ol" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre")
                    NewParagraph(name == "blockquote" ? 12 : 0);
                else if (name == "li") NewParagraph();
                continue;
            }

            if (IsBlockTag(name))
            {
                NewParagraph(name == "blockquote" ? 12 : 0);
                frames.Push(new StyleFrame(name, style, activeUri));
                style = IsFormattingTag(name)
                    ? ApplyTag(name, attributes, style)
                    : ApplyCss(style, AttributeValue(attributes, "style"));
                continue;
            }
            if (name == "br")
            {
                paragraph.Inlines.Add(new LineBreak());
                continue;
            }
            if (name == "li")
            {
                NewParagraph();
                AddRun("• ");
                continue;
            }
            if (name == "a")
            {
                var href = AttributeValue(attributes, "href");
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
                {
                    if (absolute.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(absolute.UserInfo))
                        continue;
                    frames.Push(new StyleFrame(name, style, activeUri));
                    activeUri = absolute;
                    activeLink = null;
                    EnsureLink();
                }
                else if (Uri.TryCreate(SchoologyBaseUri, href, out var relative) &&
                    (relative.Scheme is "http" or "https") && string.IsNullOrEmpty(relative.UserInfo))
                {
                    frames.Push(new StyleFrame(name, style, activeUri));
                    activeUri = relative;
                    activeLink = null;
                    EnsureLink();
                }
                continue;
            }
            if (IsFormattingTag(name))
            {
                frames.Push(new StyleFrame(name, style, activeUri));
                style = ApplyTag(name, attributes, style);
            }
        }

        return block;
    }
}
