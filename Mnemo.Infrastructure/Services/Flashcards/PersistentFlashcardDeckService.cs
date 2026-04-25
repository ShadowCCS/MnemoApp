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
    private readonly IFlashcardSchedulerResolver _schedulerResolver;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FlashcardState? _state;

    public PersistentFlashcardDeckService(
        IStorageProvider storageProvider,
        ILoggerService logger,
        IFlashcardSchedulerResolver schedulerResolver)
    {
        _storageProvider = storageProvider;
        _logger = logger;
        _schedulerResolver = schedulerResolver;
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
    public async Task SaveFolderAsync(FlashcardFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Folders.FindIndex(f => string.Equals(f.Id, folder.Id, StringComparison.Ordinal));
            var snapshot = folder with { };
            if (index >= 0)
                state.Folders[index] = snapshot;
            else
                state.Folders.Add(snapshot);

            await PersistStateCoreAsync(state).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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

            if (sessionResult.SessionConfig.SessionType == FlashcardSessionType.Review)
            {
                var scheduler = _schedulerResolver.Resolve(deck.SchedulingAlgorithm);
                foreach (var result in sessionResult.CardResults)
                {
                    if (!cards.TryGetValue(result.CardId, out var card))
                        continue;
                    cards[result.CardId] = scheduler.ApplyGrade(card, result.Grade, result.ReviewedAt);
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

    /// <inheritdoc />
    public async Task<bool> DeleteFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await GetStateCoreAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Folders.FindIndex(f => string.Equals(f.Id, folderId, StringComparison.Ordinal));
            if (index < 0)
                return false;

            state.Folders.RemoveAt(index);
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
            new("f1", "Languages", null, 0),
            new("f2", "Computer Science", null, 1),
            new("f3", "Medical", null, 2)
        };

        var day = TimeSpan.FromDays(1);
        var decks = new List<FlashcardDeck>
        {
            new(
                "d1",
                "Spanish Vocabulary",
                "f1",
                null,
                new[] { "spanish", "vocab" },
                now - day,
                85,
                new Flashcard[]
                {
                    // Animals
                    new("c1",  "d1", "El gato",        "The cat",          FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c2",  "d1", "El perro",        "The dog",          FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c3",  "d1", "El pájaro",       "The bird",         FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c4",  "d1", "El caballo",      "The horse",        FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c5",  "d1", "El pez",          "The fish",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Family
                    new("c6",  "d1", "La madre",        "The mother",       FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c7",  "d1", "El padre",        "The father",       FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c8",  "d1", "El hermano",      "The brother",      FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c9",  "d1", "La hermana",      "The sister",       FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c10", "d1", "El abuelo",       "The grandfather",  FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Food & Drink
                    new("c11", "d1", "El pan",          "The bread",        FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c12", "d1", "La manzana",      "The apple",        FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c13", "d1", "El agua",         "The water",        FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c14", "d1", "La leche",        "The milk",         FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c15", "d1", "El café",         "The coffee",       FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c16", "d1", "La naranja",      "The orange",       FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c17", "d1", "El queso",        "The cheese",       FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c18", "d1", "La carne",        "The meat",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Places
                    new("c19", "d1", "La casa",         "The house",        FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c20", "d1", "La escuela",      "The school",       FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c21", "d1", "El hospital",     "The hospital",     FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c22", "d1", "La tienda",       "The shop/store",   FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c23", "d1", "El banco",        "The bank",         FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c24", "d1", "La biblioteca",   "The library",      FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Colors
                    new("c25", "d1", "Rojo",            "Red",              FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c26", "d1", "Azul",            "Blue",             FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c27", "d1", "Verde",           "Green",            FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c28", "d1", "Amarillo",        "Yellow",           FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c29", "d1", "Negro",           "Black",            FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c30", "d1", "Blanco",          "White",            FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Common Verbs
                    new("c31", "d1", "Hablar",          "To speak",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c32", "d1", "Comer",           "To eat",           FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c33", "d1", "Beber",           "To drink",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c34", "d1", "Correr",          "To run",           FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c35", "d1", "Dormir",          "To sleep",         FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c36", "d1", "Trabajar",        "To work",          FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c37", "d1", "Escribir",        "To write",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c38", "d1", "Leer",            "To read",          FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),

                    // Numbers
                    new("c39", "d1", "Uno",             "One",              FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c40", "d1", "Dos",             "Two",              FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c41", "d1", "Tres",            "Three",            FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c42", "d1", "Cuatro",          "Four",             FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c43", "d1", "Cinco",           "Five",             FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),

                    // Days of the Week
                    new("c44", "d1", "El lunes",        "Monday",           FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c45", "d1", "El martes",       "Tuesday",          FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                    new("c46", "d1", "El miércoles",    "Wednesday",        FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c47", "d1", "El jueves",       "Thursday",         FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c48", "d1", "El viernes",      "Friday",           FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),

                    // Common Adjectives
                    new("c49", "d1", "Grande",          "Big/Large",        FlashcardType.Classic, Array.Empty<string>(), now,         null, null, null),
                    new("c50", "d1", "Pequeño",         "Small/Little",     FlashcardType.Classic, Array.Empty<string>(), now + day,   null, null, null),
                },
                FlashcardSchedulingAlgorithm.Fsrs),
            new(
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
            new(
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
