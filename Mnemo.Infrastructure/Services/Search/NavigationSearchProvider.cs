using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class NavigationSearchProvider : ISearchProvider
{
    private readonly ISidebarService _sidebarService;

    public NavigationSearchProvider(ISidebarService sidebarService)
    {
        _sidebarService = sidebarService;
    }

    public string ProviderId => "navigation";
    public string GroupKey => "navigation";
    public string GroupDisplayName => "Navigate to";
    public int GroupOrder => 0;

    public Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResultItem>();
        foreach (var category in _sidebarService.Categories)
        {
            foreach (var item in category.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.IsVisible)
                {
                    continue;
                }

                var routeText = $"{item.Label}\n{item.Route}\n{category.Name}";
                if (!TextSearchMatch.MatchTokens(routeText, query.Tokens, query.MatchAllTokens, query.Fuzzy))
                {
                    continue;
                }

                results.Add(new SearchResultItem
                {
                    Id = $"nav:{item.Route}",
                    Type = SearchResultType.Navigation,
                    ProviderId = ProviderId,
                    Title = $"Navigate to {item.Label}",
                    Subtitle = category.Name,
                    Preview = item.Route,
                    GroupName = GroupDisplayName,
                    GroupId = item.Route,
                    Score = ComputeScore(item, query.Text),
                    NavigationTarget = new SearchNavigationTarget
                    {
                        Route = item.Route,
                        Href = item.Route
                    },
                    Href = item.Route
                });
            }
        }

        return Task.FromResult<IReadOnlyList<SearchResultItem>>(results);
    }

    private static double ComputeScore(Mnemo.Core.Models.SidebarItem item, string queryText)
    {
        if (string.Equals(item.Label, queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        if (item.Label.StartsWith(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            return 0.85;
        }

        if (item.Label.Contains(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            return 0.7;
        }

        if (item.Route.Contains(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            return 0.6;
        }

        return 0.45;
    }
}
