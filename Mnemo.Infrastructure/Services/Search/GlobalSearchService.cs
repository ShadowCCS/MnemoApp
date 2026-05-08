using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services.Search;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class GlobalSearchService : IGlobalSearchService
{
    private readonly IReadOnlyList<ISearchProvider> _providers;

    public GlobalSearchService(IEnumerable<ISearchProvider> providers)
    {
        _providers = providers.ToList();
    }

    public async Task<GlobalSearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || query.Tokens.Count == 0)
        {
            return new GlobalSearchResponse
            {
                BestMatches = [],
                Groups = []
            };
        }

        var searchTasks = _providers
            .Select(async provider => new
            {
                Provider = provider,
                Items = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false)
            })
            .ToArray();

        var providerResults = await Task.WhenAll(searchTasks).ConfigureAwait(false);
        var groups = new List<SearchResultGroup>(providerResults.Length);
        var allItems = new List<SearchResultItem>();

        foreach (var result in providerResults)
        {
            var orderedItems = result.Items
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Title, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            allItems.AddRange(orderedItems);

            var take = query.GetLimitForGroup(result.Provider.GroupKey);
            var visibleItems = orderedItems.Take(take).ToList();

            groups.Add(new SearchResultGroup
            {
                GroupKey = result.Provider.GroupKey,
                GroupDisplayName = result.Provider.GroupDisplayName,
                GroupOrder = result.Provider.GroupOrder,
                ResultType = orderedItems.FirstOrDefault()?.Type ?? SearchResultType.Unknown,
                Items = visibleItems,
                TotalMatched = orderedItems.Count
            });
        }

        var filteredGroups = groups
            .Where(group => group.TotalMatched > 0)
            .OrderBy(group => group.GroupOrder)
            .ThenBy(group => group.GroupDisplayName, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clusterItems = BuildTopicClusters(allItems, query);
        if (clusterItems.Count > 0)
        {
            var clusterTake = query.GetLimitForGroup("topic-clusters");
            filteredGroups.Insert(1, new SearchResultGroup
            {
                GroupKey = "topic-clusters",
                GroupDisplayName = "Topic clusters",
                GroupOrder = 1,
                ResultType = SearchResultType.DeckCardSummary,
                Items = clusterItems.Take(clusterTake).ToList(),
                TotalMatched = clusterItems.Count
            });
        }

        var flashcardMatches = allItems
            .Where(item => item.Type == SearchResultType.Flashcard && !string.IsNullOrWhiteSpace(item.GroupId))
            .OrderBy(item => item.GroupName, System.StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.Score)
            .ToList();
        if (flashcardMatches.Count > 0 && IsSpecificQuery(query))
        {
            var cardTake = query.GetLimitForGroup("cards-inside-decks");
            filteredGroups.Add(new SearchResultGroup
            {
                GroupKey = "cards-inside-decks",
                GroupDisplayName = "Cards inside decks",
                GroupOrder = 6,
                ResultType = SearchResultType.Flashcard,
                Items = flashcardMatches.Take(cardTake).ToList(),
                TotalMatched = flashcardMatches.Count
            });
        }

        var bestCandidates = allItems
            .Concat(clusterItems)
            .ToList();
        var bestMatches = bestCandidates
            .OrderByDescending(item => RankBestMatch(item, query, allItems))
            .ThenBy(item => item.Title, System.StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return new GlobalSearchResponse
        {
            BestMatches = bestMatches,
            Groups = filteredGroups
        };
    }

    private static bool IsSpecificQuery(SearchQuery query)
    {
        if (query.Tokens.Count >= 4)
        {
            return true;
        }

        var text = query.Text.Trim();
        if (text.EndsWith('?'))
        {
            return true;
        }

        return text.Contains(' ', StringComparison.Ordinal) &&
               (text.StartsWith("what", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("which", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("how", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("where", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("who", StringComparison.OrdinalIgnoreCase));
    }

    private static double RankBestMatch(SearchResultItem item, SearchQuery query, IReadOnlyList<SearchResultItem> allItems)
    {
        var score = item.Score;
        score += item.Type switch
        {
            SearchResultType.Navigation => 0.85,
            SearchResultType.Setting => 0.7,
            SearchResultType.Note => 0.58,
            SearchResultType.Mindmap => 0.48,
            SearchResultType.DeckCardSummary => 0.4,
            SearchResultType.Deck => 0.34,
            SearchResultType.Flashcard => 0.05,
            _ => 0.1
        };

        var isSpecific = IsSpecificQuery(query);
        if (item.Type == SearchResultType.Flashcard && !isSpecific)
        {
            score -= 0.35;
        }

        if (item.Type == SearchResultType.Navigation &&
            query.Tokens.Count <= 2 &&
            query.Text.Length <= 8 &&
            item.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.4;
        }

        if (item.Type == SearchResultType.Flashcard)
        {
            if (string.Equals(item.Title, query.Text, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.4;
            }
            else if (item.Title.StartsWith(query.Text, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
            }

            var hasDeckSummary = allItems.Any(candidate =>
                candidate.Type == SearchResultType.DeckCardSummary &&
                !string.IsNullOrWhiteSpace(candidate.GroupId) &&
                string.Equals(candidate.GroupId, item.GroupId, StringComparison.OrdinalIgnoreCase));
            if (hasDeckSummary && !isSpecific)
            {
                score -= 0.2;
            }
        }

        return score;
    }

    private static List<SearchResultItem> BuildTopicClusters(IReadOnlyList<SearchResultItem> allItems, SearchQuery query)
    {
        var flashcards = allItems
            .Where(item => item.Type == SearchResultType.Flashcard && !string.IsNullOrWhiteSpace(item.Title))
            .ToList();
        if (flashcards.Count < 2)
        {
            return [];
        }

        var grouped = flashcards
            .Select(card => new
            {
                Card = card,
                Key = BuildClusterKey(card.Title)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .ToList();

        var clusters = new List<SearchResultItem>(grouped.Count);
        foreach (var group in grouped)
        {
            var items = group.Select(entry => entry.Card).ToList();
            var representative = items
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .First();
            var dominantDeckId = items
                .Where(item => !string.IsNullOrWhiteSpace(item.GroupId))
                .GroupBy(item => item.GroupId!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(deckGroup => deckGroup.Count())
                .Select(deckGroup => deckGroup.Key)
                .FirstOrDefault();
            var dominantDeckName = items
                .Where(item => string.Equals(item.GroupId, dominantDeckId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.GroupName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? representative.GroupName;
            var score = items.Average(item => item.Score) + Math.Min(0.35, items.Count * 0.06);

            clusters.Add(new SearchResultItem
            {
                Id = $"cluster:{group.Key}",
                Type = SearchResultType.DeckCardSummary,
                ProviderId = "topic-clusters",
                Title = ToDisplayClusterTitle(group.Key),
                Subtitle = $"{items.Count} matching cards",
                Preview = string.IsNullOrWhiteSpace(dominantDeckName) ? "Topic cluster" : dominantDeckName,
                GroupName = "Topic clusters",
                GroupId = dominantDeckId,
                Score = score,
                NavigationTarget = string.IsNullOrWhiteSpace(dominantDeckId)
                    ? null
                    : new SearchNavigationTarget
                    {
                        Route = "flashcard-deck",
                        Parameter = new FlashcardSearchNavigationParameter(dominantDeckId, query.Text),
                        Href = "flashcard-deck"
                    },
                Href = string.IsNullOrWhiteSpace(dominantDeckId) ? null : "flashcard-deck",
                Payload = items.Select(item => item.Id).ToArray()
            });
        }

        return clusters
            .OrderByDescending(cluster => cluster.Score)
            .ThenBy(cluster => cluster.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? BuildClusterKey(string title)
    {
        var normalized = title.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = Regex.Replace(normalized, "[^a-z0-9\\s]", " ");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        normalized = StripQuestionLead(normalized);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2 && !ClusterStopWords.Contains(token))
            .Take(4)
            .ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        return string.Join(' ', tokens);
    }

    private static string StripQuestionLead(string text)
    {
        foreach (var prefix in QuestionPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[prefix.Length..].Trim();
            }
        }

        return text;
    }

    private static string ToDisplayClusterTitle(string key)
    {
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "Topic cluster";
        }

        return string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static readonly string[] QuestionPrefixes =
    [
        "what is",
        "what are",
        "which is",
        "which",
        "how many",
        "how much",
        "where is",
        "who is"
    ];

    private static readonly HashSet<string> ClusterStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the",
        "and",
        "for",
        "with",
        "from",
        "into",
        "that",
        "this",
        "your",
        "are"
    };
}
