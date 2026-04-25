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
    /// Creates or updates a folder snapshot.
    /// </summary>
    Task SaveFolderAsync(FlashcardFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a deck by id, or <c>null</c> if it does not exist.
    /// </summary>
    Task<FlashcardDeck?> GetDeckByIdAsync(string deckId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored deck with the given snapshot (full replace).
    /// </summary>
    Task SaveDeckAsync(FlashcardDeck deck, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records and applies a completed practice session outcome.
    /// </summary>
    Task RecordSessionOutcomeAsync(
        FlashcardSessionResult sessionResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns retention trend points for sparkline rendering.
    /// </summary>
    Task<IReadOnlyList<FlashcardRetentionTrendPoint>> GetDeckRetentionTrendAsync(
        string deckId,
        int days = 14,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a deck by id. Returns <c>false</c> if the deck was not found.
    /// </summary>
    Task<bool> DeleteDeckAsync(string deckId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a folder by id. Returns <c>false</c> if the folder was not found.
    /// </summary>
    Task<bool> DeleteFolderAsync(string folderId, CancellationToken cancellationToken = default);
}
