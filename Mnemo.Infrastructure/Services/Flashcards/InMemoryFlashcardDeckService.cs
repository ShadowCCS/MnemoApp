using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

/// <summary>
/// In-memory flashcard store for UI development and tests.
/// </summary>
public sealed class InMemoryFlashcardDeckService : IFlashcardDeckService
{
    private readonly IFlashcardSchedulerResolver _schedulerResolver;
    private readonly object _gate = new();
    private readonly List<FlashcardFolder> _folders;
    private readonly List<FlashcardDeck> _decks;
    private readonly List<FlashcardSessionResult> _sessionHistory = new();

    public InMemoryFlashcardDeckService(IFlashcardSchedulerResolver schedulerResolver)
    {
        _schedulerResolver = schedulerResolver;
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
    public Task SaveFolderAsync(FlashcardFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _folders.FindIndex(f => string.Equals(f.Id, folder.Id, StringComparison.Ordinal));
            var snapshot = folder with { };
            if (index >= 0)
                _folders[index] = snapshot;
            else
                _folders.Add(snapshot);
        }

        return Task.CompletedTask;
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
        FlashcardSessionResult sessionResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionResult);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var deckIndex = _decks.FindIndex(d => string.Equals(d.Id, sessionResult.DeckId, StringComparison.Ordinal));
            if (deckIndex < 0)
                return Task.CompletedTask;

            var existing = _decks[deckIndex];
            var cards = existing.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            if (sessionResult.SessionConfig.SessionType == FlashcardSessionType.Review)
            {
                var scheduler = _schedulerResolver.Resolve(existing.SchedulingAlgorithm);
                foreach (var result in sessionResult.CardResults)
                {
                    if (!cards.TryGetValue(result.CardId, out var current))
                        continue;

                    cards[result.CardId] = scheduler.ApplyGrade(current, result.Grade, result.ReviewedAt);
                }
            }

            _sessionHistory.Add(sessionResult with { CardResults = sessionResult.CardResults.ToArray() });
            _decks[deckIndex] = existing with
            {
                Cards = cards.Values.ToArray(),
                LastStudied = sessionResult.CompletedAt,
                RetentionScore = ComputeRetentionPercentForDeck(sessionResult.DeckId)
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FlashcardRetentionTrendPoint>> GetDeckRetentionTrendAsync(
        string deckId,
        int days = 14,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        days = Math.Clamp(days, 1, 90);

        lock (_gate)
        {
            var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(days - 1)));
            var grouped = _sessionHistory
                .Where(s => string.Equals(s.DeckId, deckId, StringComparison.Ordinal))
                .SelectMany(s => s.CardResults)
                .GroupBy(r => DateOnly.FromDateTime(r.ReviewedAt.UtcDateTime.Date))
                .ToDictionary(
                    g => g.Key,
                    g => new FlashcardRetentionTrendPoint(g.Key, ComputeRetentionPercent(g), g.Count()));

            var points = new List<FlashcardRetentionTrendPoint>(days);
            for (var i = 0; i < days; i++)
            {
                var day = start.AddDays(i);
                points.Add(grouped.TryGetValue(day, out var point)
                    ? point
                    : new FlashcardRetentionTrendPoint(day, 0, 0));
            }

            return Task.FromResult<IReadOnlyList<FlashcardRetentionTrendPoint>>(points);
        }
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
            _sessionHistory.RemoveAll(s => string.Equals(s.DeckId, deckId, StringComparison.Ordinal));
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _folders.FindIndex(f => string.Equals(f.Id, folderId, StringComparison.Ordinal));
            if (index < 0)
                return Task.FromResult(false);

            _folders.RemoveAt(index);
            return Task.FromResult(true);
        }
    }

    private static (List<FlashcardFolder> Folders, List<FlashcardDeck> Decks) CreateSeedData(DateTimeOffset now)
    {
        var folders = new List<FlashcardFolder>
        {
            new("f1", "Languages", null, 0),
            new("f2", "Computer Science", null, 1),
            new("f3", "Medical", null, 2)
        };

        var day = TimeSpan.FromDays(1);
        var decks = new List<FlashcardDeck>
        {
            new FlashcardDeck(
                "d1",
                "Spanish Vocabulary",
                "f1",
                null,
                new[] { "spanish", "vocab" },
                now - day,
                85,
                new Flashcard[]
                {
                    new("c1", "d1", "El gato", "The cat", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c2", "d1", "El perro", "The dog", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c3", "d1", "La casa", "The house", FlashcardType.Classic, Array.Empty<string>(), now + day, null, null, null)
                },
                FlashcardSchedulingAlgorithm.Fsrs),
            new FlashcardDeck(
                "d2",
                "Data Structures",
                "f2",
                null,
                new[] { "cs", "algorithms" },
                now - TimeSpan.FromDays(2),
                62,
                new Flashcard[]
                {
                    new("c4", "d2", "What is a Hash Table?", "A data structure that implements an associative array abstract data type, a structure that can map keys to values.", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c5", "d2", "Time complexity of Binary Search", "O(log n)", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null),
                    new("c6", "d2", "{{c1::Quick Sort}} has an average time complexity of O(n log n).", "Quick Sort", FlashcardType.Cloze, Array.Empty<string>(), now, null, null, null)
                },
                FlashcardSchedulingAlgorithm.Sm2),
            new FlashcardDeck(
                "d3",
                "Anatomy: Bones",
                "f3",
                null,
                new[] { "anatomy", "biology" },
                null,
                0,
                new Flashcard[]
                {
                    new("c7", "d3", "Longest bone in the human body", "Femur", FlashcardType.Classic, Array.Empty<string>(), now, null, null, null)
                },
                FlashcardSchedulingAlgorithm.Leitner)
        };

        return (folders, decks);
    }

    private static FlashcardDeck CloneDeck(FlashcardDeck deck)
    {
        var cards = deck.Cards.Select(c => c with { Tags = c.Tags.ToArray() }).ToList();
        return deck with { Tags = deck.Tags.ToArray(), Cards = cards };
    }

    private int ComputeRetentionPercentForDeck(string deckId)
    {
        var results = _sessionHistory
            .Where(s => string.Equals(s.DeckId, deckId, StringComparison.Ordinal))
            .SelectMany(s => s.CardResults)
            .TakeLast(200);
        return ComputeRetentionPercent(results);
    }

    private static int ComputeRetentionPercent(IEnumerable<FlashcardSessionCardResult> results)
    {
        var list = results.ToList();
        if (list.Count == 0)
            return 0;

        var weightedScore = list.Sum(r => r.Grade switch
        {
            FlashcardReviewGrade.Again => 0d,
            FlashcardReviewGrade.Hard => 0.4d,
            FlashcardReviewGrade.Good => 0.75d,
            FlashcardReviewGrade.Easy => 1d,
            _ => 0d
        });
        var pct = weightedScore / list.Count * 100d;
        return (int)Math.Clamp(Math.Round(pct, MidpointRounding.AwayFromZero), 0, 100);
    }
}
