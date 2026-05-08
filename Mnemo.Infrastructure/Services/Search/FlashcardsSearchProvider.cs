using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class FlashcardsSearchProvider : ISearchProvider
{
    private readonly IFlashcardDeckService _flashcardDeckService;

    public FlashcardsSearchProvider(IFlashcardDeckService flashcardDeckService)
    {
        _flashcardDeckService = flashcardDeckService;
    }

    public string ProviderId => "flashcards";
    public string GroupKey => "flashcards";
    public string GroupDisplayName => "Flashcards";
    public int GroupOrder => 2;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var decks = await _flashcardDeckService.ListDecksAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SearchResultItem>();

        foreach (var deck in decks)
        {
            foreach (var card in deck.Cards)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var haystack = $"{card.Front}\n{card.Back}\n{string.Join(' ', card.Tags)}\n{deck.Name}";
                if (!TextSearchMatch.MatchTokens(haystack, query.Tokens, query.MatchAllTokens, query.Fuzzy))
                {
                    continue;
                }

                var score = ComputeCardScore(deck, card, query);
                var preview = BuildPreview(card);
                var subtitle = string.IsNullOrWhiteSpace(deck.Name) ? null : $"Deck: {deck.Name}";

                results.Add(new SearchResultItem
                {
                    Id = card.Id,
                    Type = SearchResultType.Flashcard,
                    ProviderId = ProviderId,
                    Title = card.Front,
                    Subtitle = subtitle,
                    Preview = preview,
                    GroupName = deck.Name,
                    GroupId = deck.Id,
                    Score = score,
                    NavigationTarget = new SearchNavigationTarget
                    {
                        Route = "flashcard-deck",
                        Parameter = new FlashcardSearchNavigationParameter(deck.Id, query.Text, card.Id),
                        Href = "flashcard-deck"
                    },
                    Href = "flashcard-deck",
                    Flashcard = new FlashcardSearchMetadata
                    {
                        DeckId = deck.Id,
                        DeckTitle = deck.Name,
                        FrontText = card.Front,
                        BackText = card.Back,
                        Tags = card.Tags.ToArray()
                    },
                    Payload = card.Id
                });
            }
        }

        return results;
    }

    private static string? BuildPreview(Flashcard card)
    {
        if (!string.IsNullOrWhiteSpace(card.Back))
        {
            return card.Back.Length <= 140 ? card.Back : $"{card.Back[..140]}...";
        }

        return null;
    }

    private static double ComputeCardScore(FlashcardDeck deck, Flashcard card, SearchQuery query)
    {
        var q = query.Text;
        var score = 0d;

        if (string.Equals(card.Front, q, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 1.0;
        }
        else if (card.Front.StartsWith(q, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.85;
        }
        else if (card.Front.Contains(q, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.65;
        }

        if (deck.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.45;
        }

        if (!string.IsNullOrWhiteSpace(card.Back) &&
            card.Back.Contains(q, System.StringComparison.OrdinalIgnoreCase))
        {
            score += 0.35;
        }

        if (card.Tags.Any(tag => tag.Contains(q, System.StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.2;
        }

        if (card.LastReviewedAt.HasValue)
        {
            var recencyBoost = 1d / (1d + (System.DateTimeOffset.UtcNow - card.LastReviewedAt.Value).TotalDays);
            score += recencyBoost * 0.15;
        }

        return System.Math.Clamp(score, 0d, 2d);
    }
}
