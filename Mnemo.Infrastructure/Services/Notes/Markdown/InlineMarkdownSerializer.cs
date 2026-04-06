using System.Collections.Generic;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.Notes.Markdown;

/// <summary>Serializes <see cref="InlineRun"/> lists to CommonMark-style inline markdown.</summary>
public static class InlineMarkdownSerializer
{
    public static string SerializeRuns(IReadOnlyList<InlineRun> runs)
    {
        if (runs.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var r in runs)
            sb.Append(SerializeRun(r));
        return sb.ToString();
    }

    private static string SerializeRun(InlineRun r)
    {
        if (r.Text.Length == 0)
            return string.Empty;

        if (r.Style.Code)
            return SerializeCodeSpan(r.Text);

        var escaped = EscapeMarkdownText(r.Text);
        var s = escaped;
        if (r.Style.Bold && r.Style.Italic)
            s = "***" + s + "***";
        else if (r.Style.Bold)
            s = "**" + s + "**";
        else if (r.Style.Italic)
            s = "*" + s + "*";
        if (r.Style.Strikethrough)
            s = "~~" + s + "~~";
        if (!string.IsNullOrEmpty(r.Style.LinkUrl))
            s = "[" + s + "](" + EscapeMarkdownLinkDestination(r.Style.LinkUrl) + ")";
        return s;
    }

    private static string EscapeMarkdownLinkDestination(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return url.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
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
}
