namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Builds the ordered list of cards for a practice session from a deck and session configuration.
/// </summary>
public static class FlashcardSessionQueueBuilder
{
    /// <summary>
    /// Builds the session queue. <paramref name="random"/> is used for Cram shuffle when <see cref="FlashcardSessionConfig.Shuffle"/> is true.
    /// </summary>
    public static IReadOnlyList<Flashcard> BuildQueue(
        FlashcardDeck deck,
        FlashcardSessionConfig config,
        DateTimeOffset now,
        Random? random)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(config);

        if (!string.Equals(deck.Id, config.DeckId, StringComparison.Ordinal))
            throw new ArgumentException("Deck id does not match session configuration.", nameof(config));

        var cards = deck.Cards.ToList();

        switch (config.SessionType)
        {
            case FlashcardSessionType.Review:
            case FlashcardSessionType.Quick:
                cards = cards
                    .Where(c => c.DueDate <= now)
                    .OrderBy(c => c.DueDate)
                    .ThenBy(c => c.Retrievability ?? 0.5d)
                    .ToList();
                break;
            case FlashcardSessionType.Focused:
                cards = cards
                    .OrderBy(c => c.DueDate <= now ? 0 : 1)
                    .ThenBy(c => c.DueDate)
                    .ThenBy(c => c.Retrievability ?? 0.5d)
                    .ThenBy(c => c.Stability ?? 0d)
                    .ToList();
                if (config.CardCount is { } count && count > 0)
                    cards = cards.Take(count).ToList();
                break;
            case FlashcardSessionType.Cram:
                cards = cards
                    .OrderBy(c => c.DueDate <= now ? 0 : 1)
                    .ThenBy(c => c.DueDate)
                    .ToList();
                if (config.Shuffle)
                    Shuffle(cards, random ?? new Random());
                break;
            case FlashcardSessionType.Test:
                cards = cards
                    .OrderBy(c => c.DueDate <= now ? 0 : 1)
                    .ThenBy(c => c.DueDate)
                    .ThenBy(c => c.Retrievability ?? 0.5d)
                    .ToList();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(config));
        }

        return cards;
    }

    /// <summary>
    /// Estimates focused card count from a time-box if no explicit card count is set.
    /// Uses a conservative average pace of 45 seconds/card.
    /// </summary>
    public static int? EstimateFocusedCardCount(FlashcardSessionConfig config)
    {
        if (config.SessionType != FlashcardSessionType.Focused)
            return null;
        if (config.CardCount is > 0)
            return config.CardCount;
        if (config.TimeLimitMinutes is not > 0)
            return null;

        return Math.Max(1, (int)Math.Floor(config.TimeLimitMinutes.Value * 60d / 45d));
    }

    private static void Shuffle(IList<Flashcard> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
