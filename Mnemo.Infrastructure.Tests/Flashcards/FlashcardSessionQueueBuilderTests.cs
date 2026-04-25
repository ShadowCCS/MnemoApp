using Mnemo.Core.Models.Flashcards;

namespace Mnemo.Infrastructure.Tests.Flashcards;

public sealed class FlashcardSessionQueueBuilderTests
{
    [Fact]
    public void BuildQueue_Quick_IncludesOnlyCardsDueBeforeNow()
    {
        var now = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero);
        var deck = new FlashcardDeck(
            "deck-1",
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            0,
            new[]
            {
                new Flashcard("a", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddMinutes(-1), null, null, null),
                new Flashcard("n", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                new Flashcard("b", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddMinutes(1), null, null, null)
            });
        var config = new FlashcardSessionConfig(FlashcardSessionType.Quick, "deck-1", null, null, false, null);

        var queue = FlashcardSessionQueueBuilder.BuildQueue(deck, config, now, null);

        Assert.Equal(2, queue.Count);
        Assert.Equal("a", queue[0].Id);
        Assert.Equal("n", queue[1].Id);
    }

    [Fact]
    public void BuildQueue_Focused_LimitsToCardCount()
    {
        var now = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero);
        var deck = new FlashcardDeck(
            "deck-1",
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            0,
            new[]
            {
                new Flashcard("c0", "deck-1", "f0", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddDays(2), null, null, 0.9d),
                new Flashcard("c1", "deck-1", "f1", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddDays(-2), null, null, 0.8d),
                new Flashcard("c2", "deck-1", "f2", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddDays(-1), null, null, 0.3d),
                new Flashcard("c3", "deck-1", "f3", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddDays(1), null, null, 0.1d),
                new Flashcard("c4", "deck-1", "f4", "b", FlashcardType.Classic, Array.Empty<string>(), now.AddDays(4), null, null, 0.5d)
            });
        var config = new FlashcardSessionConfig(FlashcardSessionType.Focused, "deck-1", 2, null, false, null);

        var queue = FlashcardSessionQueueBuilder.BuildQueue(deck, config, now, null);

        Assert.Equal(2, queue.Count);
        Assert.Equal("c1", queue[0].Id);
        Assert.Equal("c2", queue[1].Id);
    }

    [Fact]
    public void BuildQueue_Cram_WithShuffle_IsDeterministicForSeededRandom()
    {
        var now = DateTimeOffset.UtcNow;
        var deck = new FlashcardDeck(
            "deck-1",
            "Test",
            null,
            null,
            Array.Empty<string>(),
            null,
            0,
            new[]
            {
                new Flashcard("x", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                new Flashcard("y", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                new Flashcard("z", "deck-1", "f", "b", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null)
            });
        var config = new FlashcardSessionConfig(FlashcardSessionType.Cram, "deck-1", null, null, true, null);
        var random = new Random(12345);

        var queue = FlashcardSessionQueueBuilder.BuildQueue(deck, config, now, random).ToArray();
        var again = FlashcardSessionQueueBuilder.BuildQueue(deck, config, now, new Random(12345)).ToArray();

        Assert.Equal(3, queue.Length);
        Assert.Equal(queue.Select(c => c.Id), again.Select(c => c.Id));
    }

    [Fact]
    public void BuildQueue_DeckIdMismatch_ThrowsArgumentException()
    {
        var now = DateTimeOffset.UtcNow;
        var deck = new FlashcardDeck("a", "N", null, null, Array.Empty<string>(), null, 0, Array.Empty<Flashcard>());
        var config = new FlashcardSessionConfig(FlashcardSessionType.Test, "b", null, null, false, null);

        Assert.Throws<ArgumentException>(() => FlashcardSessionQueueBuilder.BuildQueue(deck, config, now, null));
    }
}
