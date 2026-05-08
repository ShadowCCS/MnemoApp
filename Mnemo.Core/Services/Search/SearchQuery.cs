using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.Core.Services.Search;

public sealed class SearchQuery
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> Tokens { get; init; } = Array.Empty<string>();
    public int LimitPerGroup { get; init; } = 10;
    public IReadOnlyDictionary<string, int>? GroupLimits { get; init; }
    public bool Fuzzy { get; init; } = true;
    public bool MatchAllTokens { get; init; }

    public int GetLimitForGroup(string groupKey)
    {
        if (GroupLimits != null &&
            GroupLimits.TryGetValue(groupKey, out var groupLimit) &&
            groupLimit > 0)
        {
            return groupLimit;
        }

        return LimitPerGroup > 0 ? LimitPerGroup : 10;
    }

    public static SearchQuery Create(
        string text,
        int limitPerGroup = 10,
        IReadOnlyDictionary<string, int>? groupLimits = null,
        bool fuzzy = true,
        bool matchAllTokens = false)
    {
        var normalized = text?.Trim() ?? string.Empty;
        var tokens = normalized
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SearchQuery
        {
            Text = normalized,
            Tokens = tokens,
            LimitPerGroup = limitPerGroup,
            GroupLimits = groupLimits,
            Fuzzy = fuzzy,
            MatchAllTokens = matchAllTokens
        };
    }
}
