using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.Notes.Markdown;

/// <summary>Serializes <see cref="InlineSpan"/> lists to CommonMark-style inline markdown.</summary>
public static class InlineMarkdownSerializer
{
    private static readonly Regex EmbeddedImageRegex = new(
        @"!\[[^\]]*\]\([^)]+\)(?:\{align=(?:left|center|right)\})?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string SerializeSpans(IReadOnlyList<InlineSpan> spans)
    {
        if (spans.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var s in spans)
            sb.Append(SerializeSpan(s));
        return sb.ToString();
    }

    private static string SerializeSpan(InlineSpan s) => s switch
    {
        EquationSpan e => "$" + e.Latex + "$",
        FractionSpan f => $"\\{f.Numerator}/{f.Denominator}",
        TextSpan t => SerializeTextSpan(t),
        _ => string.Empty
    };

    private static string SerializeTextSpan(TextSpan r)
    {
        if (r.Text.Length == 0)
            return string.Empty;

        if (r.Style.Code)
            return SerializeCodeSpan(r.Text);

        var escaped = EscapeMarkdownTextPreservingEmbeddedImages(r.Text);
        var o = escaped;
        if (r.Style.Bold && r.Style.Italic)
            o = "***" + o + "***";
        else if (r.Style.Bold)
            o = "**" + o + "**";
        else if (r.Style.Italic)
            o = "*" + o + "*";
        if (r.Style.Strikethrough)
            o = "~~" + o + "~~";
        if (!string.IsNullOrEmpty(r.Style.LinkUrl))
            o = "[" + o + "](" + EscapeMarkdownLinkDestination(r.Style.LinkUrl) + ")";
        return o;
    }

    private static string EscapeMarkdownLinkDestination(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return url.Replace("\\", "\\\\", System.StringComparison.Ordinal).Replace(")", "\\)", System.StringComparison.Ordinal);
    }

    private static string SerializeCodeSpan(string text)
    {
        var maxRun = MaxConsecutiveBackticks(text);
        var fenceLen = maxRun + 1;
        var fence = new string('`', fenceLen);
        var pad = maxRun > 0 ? " " : string.Empty;
        return fence + pad + text + pad + fence;
    }

    private static int MaxConsecutiveBackticks(string text)
    {
        var max = 0;
        var cur = 0;
        foreach (var c in text)
        {
            if (c == '`') { cur++; max = System.Math.Max(max, cur); }
            else cur = 0;
        }
        return max;
    }

    private static string EscapeMarkdownText(string text)
    {
        if (text.Length == 0) return text;
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                case '*':
                case '_':
                case '~':
                case '`':
                case '[':
                case ']':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                case '\n':
                    sb.AppendLine();
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeMarkdownTextPreservingEmbeddedImages(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var matches = EmbeddedImageRegex.Matches(text);
        if (matches.Count == 0)
            return EscapeMarkdownText(text);

        var sb = new StringBuilder(text.Length + 16);
        var cursor = 0;
        foreach (Match match in matches)
        {
            if (match.Index > cursor)
                sb.Append(EscapeMarkdownText(text[cursor..match.Index]));
            sb.Append(match.Value);
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            sb.Append(EscapeMarkdownText(text[cursor..]));
        return sb.ToString();
    }
}
