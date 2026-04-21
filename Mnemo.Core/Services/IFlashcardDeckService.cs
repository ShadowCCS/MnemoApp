using Mnemo.Core.Models.Flashcards;

namespace Mnemo.Core.Services;

/// <summary>
/// Provides access to flashcard decks and folders for the UI. Persistence may be added later.
/// </summary>
public interface IFlashcardDeckService
{
    /// <summary>
    /// Returns all decks visible to the user.
    /// </summary>
    Task<IReadOnlyList<FlashcardDeck>> ListDecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all folders used to organize decks.
    /// </summary>
    Task<IReadOnlyList<FlashcardFolder>> ListFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a deck by id, or <c>null</c> if it does not exist.
    /// </summary>
    Task<FlashcardDeck?> GetDeckByIdAsync(string deckId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored deck with the given snapshot (full replace).
    /// </summary>
    Task SaveDeckAsync(FlashcardDeck deck, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the outcome of a practice session (stub for future persistence and statistics).
    /// </summary>
    Task RecordSessionOutcomeAsync(
        string deckId,
        FlashcardSessionConfig sessionConfig,
        int correctCount,
        int incorrectCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a deck by id. Returns <c>false</c> if the deck was not found.
    /// </summary>
    Task<bool> DeleteDeckAsync(string deckId, CancellationToken cancellationToken = default);
}
