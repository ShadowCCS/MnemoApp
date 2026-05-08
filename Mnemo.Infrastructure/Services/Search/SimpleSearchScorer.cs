using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

internal static class SimpleSearchScorer
{
    public static double Compute(
        string title,
        string? subtitle,
        string? body,
        IReadOnlyList<string> tokens,
        bool fuzzy,
        bool matchAllTokens)
    {
        if (tokens.Count == 0)
        {
            return 0d;
        }

        var titleScore = ScoreField(title, tokens, fuzzy, matchAllTokens, exactBoost: 1.6);
        var subtitleScore = string.IsNullOrWhiteSpace(subtitle)
            ? 0d
            : ScoreField(subtitle, tokens, fuzzy, matchAllTokens, exactBoost: 1.15);
        var bodyScore = string.IsNullOrWhiteSpace(body)
            ? 0d
            : ScoreField(body, tokens, fuzzy, matchAllTokens, exactBoost: 1.0);

        var combined = (titleScore * 0.5) + (subtitleScore * 0.2) + (bodyScore * 0.3);
        return Math.Clamp(combined, 0d, 1d);
    }

    private static double ScoreField(
        string text,
        IReadOnlyList<string> tokens,
        bool fuzzy,
        bool matchAllTokens,
        double exactBoost)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0d;
        }

        var exactMatches = tokens.Count(token =>
            text.Contains(token, StringComparison.OrdinalIgnoreCase));

        var fuzzyMatches = 0;
        if (fuzzy)
        {
            foreach (var token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TextSearchMatch.MatchTokens(text, [token], matchAll: true, fuzzy: true))
                {
                    fuzzyMatches++;
                }
            }
        }

        var matched = exactMatches + (fuzzyMatches * 0.6);
        var coverage = matched / tokens.Count;

        if (matchAllTokens && exactMatches + fuzzyMatches < tokens.Count)
        {
            return 0d;
        }

        return Math.Clamp(coverage * exactBoost, 0d, 1d);
    }
}
