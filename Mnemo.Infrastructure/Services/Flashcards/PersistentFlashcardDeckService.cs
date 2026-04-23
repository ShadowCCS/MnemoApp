using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

/// <summary>
/// Persists flashcard decks and session history via <see cref="IStorageProvider"/>.
/// </summary>
public sealed class PersistentFlashcardDeckService : IFlashcardDeckService
{
    private const string StateStorageKey = "flashcards.state.v2";

    private readonly IStorageProvider _storageProvider;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FlashcardState? _state;

    public PersistentFlashcardDeckService(IStorageProvider storageProvider, ILoggerService logger)
    {
        _storageProvider = storageProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlashcardDeck>> ListDecksAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return state.Decks.Select(CloneDeck).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlashcardFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return state.Folders.Select(f => f with { }).ToArray();
    }

    /// <inheritdoc />
    public async Task<FlashcardDeck?> GetDeckByIdAsync(string deckId, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        var deck = state.Decks.FirstOrDefault(d => string.Equals(d.Id, deckId, StringComparison.Ordinal));
        return deck is null ? null : CloneDeck(deck);
    }

    /// <inheritdoc />
    public async Task SaveDeckAsync(FlashcardDeck deck, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deck);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Decks.FindIndex(d => string.Equals(d.Id, deck.Id, StringComparison.Ordinal));
            var snapshot = CloneDeck(deck);
            if (index >= 0)
                state.Decks[index] = snapshot;
            else
                state.Decks.Add(snapshot);

            await PersistStateCoreAsync(state).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordSessionOutcomeAsync(
        FlashcardSessionResult sessionResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionResult);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
            var deckIndex = state.Decks.FindIndex(d => string.Equals(d.Id, sessionResult.DeckId, StringComparison.Ordinal));
            if (deckIndex < 0)
                return;

            var deck = state.Decks[deckIndex];
            var cards = deck.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);

            if (sessionResult.SessionConfig.SessionType != FlashcardSessionType.Cram)
            {
                foreach (var result in sessionResult.CardResults)
                {
                    if (!cards.TryGetValue(result.CardId, out var card))
                        continue;
                    cards[result.CardId] = FlashcardScheduling.ApplyGrade(card, result.Grade, result.ReviewedAt);
                }
            }

            state.SessionHistory.Add(sessionResult with
            {
                CardResults = sessionResult.CardResults.ToArray()
            });

            state.Decks[deckIndex] = deck with
            {
                Cards = cards.Values.ToArray(),
                LastStudied = sessionResult.CompletedAt,
                RetentionScore = ComputeRetentionPercent(state, sessionResult.DeckId)
            };

            await PersistStateCoreAsync(state).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlashcardRetentionTrendPoint>> GetDeckRetentionTrendAsync(
        string deckId,
        int days = 14,
        CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        days = Math.Clamp(days, 1, 90);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(days - 1)));

        var grouped = state.SessionHistory
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

        return points;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDeckAsync(string deckId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Decks.FindIndex(d => string.Equals(d.Id, deckId, StringComparison.Ordinal));
            if (index < 0)
                return false;

            state.Decks.RemoveAt(index);
            state.SessionHistory.RemoveAll(s => string.Equals(s.DeckId, deckId, StringComparison.Ordinal));
            await PersistStateCoreAsync(state).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FlashcardState> GetStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FlashcardState> GetStateCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state is not null)
            return _state;

        var load = await _storageProvider.LoadAsync<FlashcardState>(StateStorageKey).ConfigureAwait(false);
        if (load.IsSuccess && load.Value is not null)
        {
            _state = load.Value;
            return _state;
        }

        var now = DateTimeOffset.UtcNow;
        var (folders, decks) = CreateSeedData(now);
        _state = new FlashcardState
        {
            Folders = folders,
            Decks = decks
        };
        await PersistStateCoreAsync(_state).ConfigureAwait(false);
        return _state;
    }

    private async Task PersistStateCoreAsync(FlashcardState state)
    {
        var save = await _storageProvider.SaveAsync(StateStorageKey, state).ConfigureAwait(false);
        if (!save.IsSuccess)
        {
            _logger.Warning("Flashcards", "Failed to persist flashcard state; continuing with in-memory snapshot.");
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
            new(
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
            new(
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
            new(
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

    private static int ComputeRetentionPercent(FlashcardState state, string deckId)
    {
        var results = state.SessionHistory
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

    private sealed class FlashcardState
    {
        public List<FlashcardFolder> Folders { get; set; } = new();

        public List<FlashcardDeck> Decks { get; set; } = new();

        public List<FlashcardSessionResult> SessionHistory { get; set; } = new();
    }
}
