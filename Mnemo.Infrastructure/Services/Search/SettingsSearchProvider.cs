using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class SettingsSearchProvider : ISearchProvider
{
    private readonly SettingsToolService _settingsToolService;

    public SettingsSearchProvider(SettingsToolService settingsToolService)
    {
        _settingsToolService = settingsToolService;
    }

    public string ProviderId => "settings";
    public string GroupKey => "settings";
    public string GroupDisplayName => "Settings";
    public int GroupOrder => 5;

    public Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResultItem>();
        var entries = _settingsToolService.GetCatalogEntries();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allowedValues = entry.AllowedValues == null ? string.Empty : string.Join(", ", entry.AllowedValues);
            var haystack = $"{entry.Key}\n{entry.Category}\n{allowedValues}";

            if (!TextSearchMatch.MatchTokens(haystack, query.Tokens, query.MatchAllTokens, query.Fuzzy))
            {
                continue;
            }

            var score = SimpleSearchScorer.Compute(entry.Key, entry.Category, allowedValues, query.Tokens, query.Fuzzy, query.MatchAllTokens);
            var subtitle = entry.Writable ? $"{entry.Category} - writable" : $"{entry.Category} - readonly";
            var snippet = string.IsNullOrWhiteSpace(allowedValues) ? null : $"Allowed: {allowedValues}";

            results.Add(new SearchResultItem
            {
                Id = entry.Key,
                Type = SearchResultType.Setting,
                ProviderId = ProviderId,
                Title = entry.Key,
                Subtitle = subtitle,
                Preview = snippet,
                GroupName = entry.Category,
                GroupId = entry.Category,
                Score = score,
                NavigationTarget = new SearchNavigationTarget
                {
                    Route = "settings",
                    Parameter = entry.Key,
                    Href = "settings"
                },
                Href = "settings",
                Payload = entry.Key
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResultItem>>(results
            .OrderByDescending(item => item.Score)
            .ToList());
    }
}
