using System;
using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>Keyword and optional fuzzy (typo-tolerant) matching for tool search strings.</summary>
public static class TextSearchMatch
{
    private static readonly char[] WordSeparators =
    {
        ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '\"', '\'', '/', '-', '_'
    };

    public static List<string> SplitSearchTokens(string q)
    {
        return q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim(',', '.', ';', ':', '"', '\'', '!', '?'))
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Tokens for search; if splitting yields nothing (e.g. one short character), returns the trimmed raw string as a single token when non-empty.</summary>
    public static IReadOnlyList<string> ResolveSearchTokens(string q)
    {
        var raw = q?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            return Array.Empty<string>();

        var list = SplitSearchTokens(raw);
        if (list.Count > 0)
            return list;

        return new[] { raw };
    }

    public static bool MatchTokens(string haystack, IReadOnlyList<string> tokens, bool matchAll, bool fuzzy)
    {
        if (tokens.Count == 0 || haystack.Length == 0)
            return false;

        if (matchAll)
            return tokens.All(t => TokenMatches(haystack, t, fuzzy));
        return tokens.Any(t => TokenMatches(haystack, t, fuzzy));
    }

    public static bool TryGetSnippetSpan(string text, IReadOnlyList<string> tokens, bool fuzzy, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (text.Length == 0 || tokens.Count == 0)
            return false;

        var bestStart = -1;
        var bestLen = 0;

        foreach (var t in tokens)
        {
            if (t.Length == 0)
                continue;

            var ix = text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
            if (ix >= 0 && (bestStart < 0 || ix < bestStart))
            {
                bestStart = ix;
                bestLen = t.Length;
            }
        }

        if (bestStart >= 0)
        {
            start = Math.Max(0, bestStart - 40);
            var end = Math.Min(text.Length, bestStart + Math.Max(bestLen, 1) + 40);
            length = end - start;
            return true;
        }

        if (!fuzzy)
            return false;

        foreach (var t in tokens)
        {
            if (t.Length < 4)
                continue;

            var max = MaxEditsForToken(t.Length);
            foreach (var (wordStart, wordLen, word) in EnumerateWordSpans(text))
            {
                if (word.Length < 3)
                    continue;
                if (LevenshteinLimited(word, t, max) <= max)
                {
                    bestStart = wordStart;
                    bestLen = wordLen;
                    start = Math.Max(0, bestStart - 40);
                    var end = Math.Min(text.Length, bestStart + bestLen + 40);
                    length = end - start;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TokenMatches(string haystack, string token, bool fuzzy)
    {
        if (token.Length == 0)
            return true;

        if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!fuzzy || token.Length < 4)
            return false;

        var max = MaxEditsForToken(token.Length);
        foreach (var (_, _, word) in EnumerateWordSpans(haystack))
        {
            if (word.Length < 3)
                continue;
            if (LevenshteinLimited(word, token, max) <= max)
                return true;
        }

        return false;
    }

    private static int MaxEditsForToken(int len)
    {
        if (len < 4)
            return 0;
        if (len <= 6)
            return 1;
        return 2;
    }

    private static IEnumerable<(int start, int length, string word)> EnumerateWordSpans(string haystack)
    {
        var i = 0;
        while (i < haystack.Length)
        {
            while (i < haystack.Length && IsSeparator(haystack[i]))
                i++;
            if (i >= haystack.Length)
                yield break;

            var start = i;
            while (i < haystack.Length && !IsSeparator(haystack[i]))
                i++;

            var len = i - start;
            var word = haystack.AsSpan(start, len).ToString();
            yield return (start, len, word);
        }
    }

    private static bool IsSeparator(char c)
    {
        if (char.IsWhiteSpace(c))
            return true;
        foreach (var s in WordSeparators)
        {
            if (s == c)
                return true;
        }

        return false;
    }

    private static int LevenshteinLimited(string a, string b, int max) =>
        LevenshteinLimited(a.AsSpan(), b.AsSpan(), max);

    private static int LevenshteinLimited(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int max)
    {
        if (a.Length == 0)
            return b.Length <= max ? b.Length : max + 1;
        if (b.Length == 0)
            return a.Length <= max ? a.Length : max + 1;

        var al = a.Length;
        var bl = b.Length;
        if (Math.Abs(al - bl) > max)
            return max + 1;

        var prev = new int[bl + 1];
        var curr = new int[bl + 1];
        for (var j = 0; j <= bl; j++)
            prev[j] = j;

        for (var i = 1; i <= al; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            var ac = char.ToLowerInvariant(a[i - 1]);
            for (var j = 1; j <= bl; j++)
            {
                var cost = ac == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                var del = prev[j] + 1;
                var ins = curr[j - 1] + 1;
                var sub = prev[j - 1] + cost;
                var m = del < ins ? del : ins;
                m = m < sub ? m : sub;
                curr[j] = m;
                if (m < rowMin)
                    rowMin = m;
            }

            if (rowMin > max)
                return max + 1;

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[bl];
    }
}
