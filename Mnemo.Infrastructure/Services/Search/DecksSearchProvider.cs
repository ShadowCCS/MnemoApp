using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class DecksSearchProvider : ISearchProvider
{
    private readonly IFlashcardDeckService _flashcardDeckService;

    public DecksSearchProvider(IFlashcardDeckService flashcardDeckService)
    {
        _flashcardDeckService = flashcardDeckService;
    }

    public string ProviderId => "decks";
    public string GroupKey => "decks";
    public string GroupDisplayName => "Decks";
    public int GroupOrder => 1;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var decks = await _flashcardDeckService.ListDecksAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SearchResultItem>();

        foreach (var deck in decks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var haystack = $"{deck.Name}\n{deck.Description}\n{string.Join(' ', deck.Tags)}";
            var titleExact = string.Equals(deck.Name, query.Text, System.StringComparison.OrdinalIgnoreCase);
            var titleStartsWith = deck.Name.StartsWith(query.Text, System.StringComparison.OrdinalIgnoreCase);
            var titleContains = deck.Name.Contains(query.Text, System.StringComparison.OrdinalIgnoreCase);
            var matchedCards = deck.Cards
                .Where(card => TextSearchMatch.MatchTokens(
                    $"{card.Front}\n{card.Back}\n{string.Join(' ', card.Tags)}",
                    query.Tokens,
                    query.MatchAllTokens,
                    query.Fuzzy))
                .ToList();

            var hasAnyMatch = titleExact || titleStartsWith || titleContains || matchedCards.Count > 0 ||
                              TextSearchMatch.MatchTokens(haystack, query.Tokens, query.MatchAllTokens, query.Fuzzy);
            if (!hasAnyMatch)
            {
                continue;
            }

            var subtitle = BuildSubtitle(deck);
            var score = ComputeDeckScore(deck, matchedCards.Count, query);
            var preview = BuildPreview(deck, matchedCards);
            var resultType = matchedCards.Count > 0 ? SearchResultType.DeckCardSummary : SearchResultType.Deck;

            results.Add(new SearchResultItem
            {
                Id = deck.Id,
                Type = resultType,
                ProviderId = ProviderId,
                Title = deck.Name,
                Subtitle = matchedCards.Count > 0 ? $"{matchedCards.Count} matching cards" : subtitle,
                Preview = preview,
                GroupName = GroupDisplayName,
                GroupId = deck.Id,
                Score = score,
                NavigationTarget = new SearchNavigationTarget
                {
                    Route = "flashcard-deck",
                    Parameter = new FlashcardSearchNavigationParameter(deck.Id, query.Text),
                    Href = "flashcard-deck"
                },
                Href = "flashcard-deck",
                Payload = deck.Id
            });
        }

        return results;
    }

    private static string? BuildSubtitle(FlashcardDeck deck)
    {
        var cardCount = deck.Cards.Count;
        if (deck.Tags.Count == 0)
        {
            return $"{cardCount} cards";
        }

        return $"{cardCount} cards - {string.Join(", ", deck.Tags)}";
    }

    private static double ComputeDeckScore(FlashcardDeck deck, int matchedCardCount, SearchQuery query)
    {
        var score = 0d;
        var queryText = query.Text;
        if (string.Equals(deck.Name, queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 1.0;
        }
        else if (deck.Name.StartsWith(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.8;
        }
        else if (deck.Name.Contains(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.6;
        }

        if (!string.IsNullOrWhiteSpace(deck.Description) &&
            deck.Description.Contains(queryText, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }

        if (deck.Tags.Any(tag => tag.Contains(queryText, System.StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.15;
        }

        score += System.Math.Min(0.75, matchedCardCount * 0.06);
        return System.Math.Clamp(score, 0d, 1.8d);
    }

    private static string? BuildPreview(FlashcardDeck deck, IReadOnlyList<Flashcard> matchedCards)
    {
        if (matchedCards.Count > 0)
        {
            var samples = matchedCards
                .Select(card => card.Front)
                .Where(front => !string.IsNullOrWhiteSpace(front))
                .Take(3)
                .ToArray();
            if (samples.Length > 0)
            {
                return string.Join(", ", samples);
            }
        }

        return string.IsNullOrWhiteSpace(deck.Description) ? null : deck.Description;
    }
}
