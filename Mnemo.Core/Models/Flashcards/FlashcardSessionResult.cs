namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Persistable summary and graded outcomes for a single practice session.
/// </summary>
public sealed record FlashcardSessionResult(
    string DeckId,
    FlashcardSessionConfig SessionConfig,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<FlashcardSessionCardResult> CardResults);
