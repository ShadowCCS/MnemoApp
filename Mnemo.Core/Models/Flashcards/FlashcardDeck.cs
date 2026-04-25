namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// A deck of flashcards with optional folder assignment and study metadata.
/// </summary>
public sealed record FlashcardDeck(
    string Id,
    string Name,
    string? FolderId,
    string? Description,
    IReadOnlyList<string> Tags,
    DateTimeOffset? LastStudied,
    int RetentionScore,
    IReadOnlyList<Flashcard> Cards,
    FlashcardSchedulingAlgorithm SchedulingAlgorithm = FlashcardSchedulingAlgorithm.Fsrs);
