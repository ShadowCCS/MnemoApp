using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

/// <summary>
/// In-memory flashcard store for UI development until SQLite persistence exists.
/// </summary>
public sealed class InMemoryFlashcardDeckService : IFlashcardDeckService
{
    private readonly object _gate = new();
    private readonly List<FlashcardFolder> _folders;
    private readonly List<FlashcardDeck> _decks;

    public InMemoryFlashcardDeckService()
    {
        var now = DateTimeOffset.UtcNow;
        (_folders, _decks) = CreateSeedData(now);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlashcardDeck>> ListDecksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<FlashcardDeck>>(_decks.Select(CloneDeck).ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlashcardFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<FlashcardFolder>>(_folders.Select(f => f with { }).ToList());
        }
    }

    /// <inheritdoc />
    public Task<FlashcardDeck?> GetDeckByIdAsync(string deckId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var deck = _decks.FirstOrDefault(d => string.Equals(d.Id, deckId, StringComparison.Ordinal));
            return Task.FromResult(deck == null ? null : CloneDeck(deck));
        }
    }

    /// <inheritdoc />
    public Task SaveDeckAsync(FlashcardDeck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _decks.FindIndex(d => string.Equals(d.Id, deck.Id, StringComparison.Ordinal));
            var snapshot = CloneDeck(deck);
            if (index >= 0)
                _decks[index] = snapshot;
            else
                _decks.Add(snapshot);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordSessionOutcomeAsync(
        string deckId,
        FlashcardSessionConfig sessionConfig,
        int correctCount,
        int incorrectCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteDeckAsync(string deckId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _decks.FindIndex(d => string.Equals(d.Id, deckId, StringComparison.Ordinal));
            if (index < 0)
                return Task.FromResult(false);

            _decks.RemoveAt(index);
            return Task.FromResult(true);
        }
    }

    private static (List<FlashcardFolder> Folders, List<FlashcardDeck> Decks) CreateSeedData(DateTimeOffset now)
    {
        var folders = new List<FlashcardFolder>
        {
            new("f1", "Languages"),
            new("f2", "Computer Science"),
            new("f3", "Medical")
        };

        var day = TimeSpan.FromDays(1);
        var decks = new List<FlashcardDeck>
        {
            new FlashcardDeck(
                "d1",
                "Spanish Vocabulary",
                "f1",
                new[] { "spanish", "vocab" },
                now - day,
                85,
                new Flashcard[]
                {
                    new("c1", "d1", "El gato", "The cat", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c2", "d1", "El perro", "The dog", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c3", "d1", "La casa", "The house", FlashcardType.Classic, Array.Empty<string>(), now + day, null, null, null)
                }),
            new FlashcardDeck(
                "d2",
                "Data Structures",
                "f2",
                new[] { "cs", "algorithms" },
                now - TimeSpan.FromDays(2),
                62,
                new Flashcard[]
                {
                    new("c4", "d2", "What is a Hash Table?", "A data structure that implements an associative array abstract data type, a structure that can map keys to values.", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c5", "d2", "Time complexity of Binary Search", "O(log n)", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c6", "d2", "{{c1::Quick Sort}} has an average time complexity of O(n log n).", "Quick Sort", FlashcardType.Cloze, Array.Empty<string>(), now, null, null, null)
                }),
            new FlashcardDeck(
                "d3",
                "Anatomy: Bones",
                "f3",
                new[] { "anatomy", "biology" },
                null,
                0,
                new Flashcard[]
                {
                    new("c7", "d3", "Longest bone in the human body", "Femur", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null)
                })
        };

        return (folders, decks);
    }

    private static FlashcardDeck CloneDeck(FlashcardDeck deck)
    {
        var cards = deck.Cards.Select(c => c with { Tags = c.Tags.ToArray() }).ToList();
        return deck with { Tags = deck.Tags.ToArray(), Cards = cards };
    }
}
