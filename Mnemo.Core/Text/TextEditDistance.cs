using System;

namespace Mnemo.Core.Text;

/// <summary>String edit distance helpers for comparing draft vs sent chat text.</summary>
public static class TextEditDistance
{
    /// <summary>Returns Levenshtein distance between two strings.</summary>
    public static int Levenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        if (m <= 256)
            return LevenshteinTwoRows(stackalloc int[m + 1], stackalloc int[m + 1], a, b);
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        return LevenshteinTwoRows(prev, curr, a, b);
    }

    private static int LevenshteinTwoRows(Span<int> prev, Span<int> curr, ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var n = a.Length;
        var m = b.Length;
        for (var j = 0; j <= m; j++)
            prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            var ai = a[i - 1];
            for (var j = 1; j <= m; j++)
            {
                var cost = ai == b[j - 1] ? 0 : 1;
                var del = prev[j] + 1;
                var ins = curr[j - 1] + 1;
                var sub = prev[j - 1] + cost;
                curr[j] = Math.Min(Math.Min(del, ins), sub);
            }

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[m];
    }

    /// <summary>
    /// True if distance is at most <paramref name="maxAbsolute"/> and at most
    /// <paramref name="maxRelativeFraction"/> of the longer string length.
    /// </summary>
    public static bool IsWithinRelativeEditDistance(string a, string b, double maxRelativeFraction, int maxAbsolute)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        var d = Levenshtein(a.AsSpan(), b.AsSpan());
        if (d > maxAbsolute)
            return false;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0)
            return true;
        return (double)d / maxLen <= maxRelativeFraction;
    }
}
