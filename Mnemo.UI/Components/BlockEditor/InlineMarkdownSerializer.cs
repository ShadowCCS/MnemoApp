using System.Collections.Generic;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>Serializes <see cref="InlineRun"/> lists to CommonMark-style inline markdown (no underline/highlight).</summary>
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
        return s;
    }

    private static string SerializeCodeSpan(string text)
    {
        int maxRun = MaxConsecutiveBackticks(text);
        int fenceLen = maxRun + 1;
        var fence = new string('`', fenceLen);
        var pad = maxRun > 0 ? " " : string.Empty;
        return fence + pad + text + pad + fence;
    }

    private static int MaxConsecutiveBackticks(string text)
    {
        int max = 0, cur = 0;
        foreach (char c in text)
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
        foreach (char c in text)
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
