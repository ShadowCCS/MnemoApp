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
            case FlashcardSessionType.Quick:
                cards = cards.Where(c => c.DueDate < now).ToList();
                break;
            case FlashcardSessionType.Focused:
                if (config.CardCount is { } count && count > 0)
                    cards = cards.Take(count).ToList();
                break;
            case FlashcardSessionType.Cram:
                if (config.Shuffle)
                    Shuffle(cards, random ?? new Random());
                break;
            case FlashcardSessionType.Test:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(config));
        }

        return cards;
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
