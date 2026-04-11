using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mnemo.Core.Models;

namespace Mnemo.Core.Formatting;

/// <summary>
/// Detects URL-like substrings in plain text and applies <see cref="InlineFormatKind.Link"/> spans.
/// Skips code runs, suppressed spans, and ranges already linked with the same href.
/// </summary>
public static class InlineAutoLink
{
    private static readonly Regex UrlRegex = new(
        @"\b(?:(?:https?|mailto):[^\s<>\[\]]+|www\.[^\s<>\[\]]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WwwPrefix = new(@"^www\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NormalizeUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;
        var t = raw.Trim();
        if (WwwPrefix.IsMatch(t))
            return "https://" + t;
        return t;
    }

    public static List<InlineSpan> Apply(IReadOnlyList<InlineSpan> spans)
    {
        if (spans.Count == 0)
            return new List<InlineSpan>();

        var flat = InlineSpanFormatApplier.Flatten(spans);
        if (flat.Length == 0)
            return InlineSpanFormatApplier.Normalize(spans);

        var matches = new List<(int Start, int End, string Url)>();
        foreach (Match m in UrlRegex.Matches(flat))
        {
            if (!m.Success) continue;
            var raw = m.Value;
            var trimmed = TrimTrailingJunk(raw);
            if (trimmed.Length == 0) continue;
            int start = m.Index;
            int end = start + trimmed.Length;
            var url = NormalizeUrl(trimmed);
            if (string.IsNullOrEmpty(url))
                continue;
            if (!CanApplyLink(spans, start, end, url))
                continue;
            matches.Add((start, end, url));
        }

        if (matches.Count == 0)
            return InlineSpanFormatApplier.Normalize(spans);

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        var filtered = new List<(int Start, int End, string Url)>();
        int lastEnd = -1;
        foreach (var x in matches)
        {
            if (x.Start < lastEnd) continue;
            filtered.Add(x);
            lastEnd = x.End;
        }

        var result = InlineSpanFormatApplier.Normalize(spans);
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            var (s, e, url) = filtered[i];
            result = InlineSpanFormatApplier.Apply(result, s, e, InlineFormatKind.Link, url);
        }

        return InlineSpanFormatApplier.Normalize(result);
    }

    private static string TrimTrailingJunk(string s)
    {
        if (s.Length == 0) return s;
        int len = s.Length;
        while (len > 0 && IsTrailingJunk(s[len - 1]))
            len--;
        return len == s.Length ? s : s[..len];
    }

    private static bool IsTrailingJunk(char c) =>
        c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '"' or '\'' or '\u201d' or '\u2019';

    private static bool CanApplyLink(IReadOnlyList<InlineSpan> spans, int start, int end, string normalizedUrl)
    {
        if (FullyLinkedWithUrl(spans, start, end, normalizedUrl))
            return false;

        int pos = 0;
        foreach (var span in spans)
        {
            int runEnd = pos + (span is TextSpan t ? t.Text.Length : 1);
            int segStart = Math.Max(start, pos);
            int segEnd = Math.Min(end, runEnd);
            if (segStart < segEnd)
            {
                if (span is not TextSpan tx)
                    return false;
                if (tx.Style.Code) return false;
                if (tx.Style.SuppressAutoLink) return false;
                if (tx.Style.LinkUrl != null
                    && !string.Equals(tx.Style.LinkUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            pos = runEnd;
        }

        return true;
    }

    private static bool FullyLinkedWithUrl(IReadOnlyList<InlineSpan> spans, int start, int end, string url)
    {
        bool any = false;
        int pos = 0;
        foreach (var span in spans)
        {
            int runEnd = pos + (span is TextSpan t ? t.Text.Length : 1);
            int segStart = Math.Max(start, pos);
            int segEnd = Math.Min(end, runEnd);
            if (segStart < segEnd)
            {
                if (span is not TextSpan tx
                    || tx.Style.LinkUrl == null
                    || !string.Equals(tx.Style.LinkUrl, url, StringComparison.OrdinalIgnoreCase))
                    return false;
                any = true;
            }

            pos = runEnd;
        }

        return any;
    }
}
