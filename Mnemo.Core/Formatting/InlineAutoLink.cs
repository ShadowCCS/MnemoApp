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
    /// <summary>
    /// https? and mailto, plus bare www. hosts (normalized with https).
    /// </summary>
    private static readonly Regex UrlRegex = new(
        @"\b(?:(?:https?|mailto):[^\s<>\[\]]+|www\.[^\s<>\[\]]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WwwPrefix = new(@"^www\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Canonical href for opening (e.g. https:// for www).</summary>
    public static string NormalizeUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;
        var t = raw.Trim();
        if (WwwPrefix.IsMatch(t))
            return "https://" + t;
        return t;
    }

    /// <summary>
    /// Returns a new normalized run list with auto-detected links applied. Never mutates the input list.
    /// </summary>
    public static List<InlineRun> Apply(IReadOnlyList<InlineRun> runs)
    {
        if (runs.Count == 0)
            return new List<InlineRun>();

        var flat = InlineRunFormatApplier.Flatten(runs);
        if (flat.Length == 0)
            return InlineRunFormatApplier.Normalize(runs);

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
            if (!CanApplyLink(runs, start, end, url))
                continue;
            matches.Add((start, end, url));
        }

        if (matches.Count == 0)
            return InlineRunFormatApplier.Normalize(runs);

        // Non-overlapping left-to-right: skip later match if it overlaps a prior one
        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        var filtered = new List<(int Start, int End, string Url)>();
        int lastEnd = -1;
        foreach (var x in matches)
        {
            if (x.Start < lastEnd) continue;
            filtered.Add(x);
            lastEnd = x.End;
        }

        var result = InlineRunFormatApplier.Normalize(runs);
        for (int i = filtered.Count - 1; i >= 0; i--)
        {
            var (s, e, url) = filtered[i];
            result = InlineRunFormatApplier.Apply(result, s, e, InlineFormatKind.Link, url);
        }

        return InlineRunFormatApplier.Normalize(result);
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
        c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '"' or '\'' or '”' or '’';

    private static bool CanApplyLink(IReadOnlyList<InlineRun> runs, int start, int end, string normalizedUrl)
    {
        if (FullyLinkedWithUrl(runs, start, end, normalizedUrl))
            return false;

        int pos = 0;
        foreach (var run in runs)
        {
            int runEnd = pos + run.Text.Length;
            int segStart = Math.Max(start, pos);
            int segEnd = Math.Min(end, runEnd);
            if (segStart < segEnd)
            {
                if (run.Style.Code) return false;
                if (run.Style.SuppressAutoLink) return false;
                if (run.Style.LinkUrl != null
                    && !string.Equals(run.Style.LinkUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            pos = runEnd;
        }

        return true;
    }

    private static bool FullyLinkedWithUrl(IReadOnlyList<InlineRun> runs, int start, int end, string url)
    {
        bool any = false;
        int pos = 0;
        foreach (var run in runs)
        {
            int runEnd = pos + run.Text.Length;
            int segStart = Math.Max(start, pos);
            int segEnd = Math.Min(end, runEnd);
            if (segStart < segEnd)
            {
                if (run.Style.LinkUrl == null
                    || !string.Equals(run.Style.LinkUrl, url, StringComparison.OrdinalIgnoreCase))
                    return false;
                any = true;
            }

            pos = runEnd;
        }

        return any;
    }
}
