namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// One graded card within a practice session.
/// </summary>
public sealed record FlashcardSessionCardResult(
    string CardId,
    FlashcardReviewGrade Grade,
    DateTimeOffset ReviewedAt);
