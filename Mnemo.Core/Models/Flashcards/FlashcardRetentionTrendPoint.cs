namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Retention point for deck-level sparkline/trend rendering.
/// </summary>
public sealed record FlashcardRetentionTrendPoint(
    DateOnly Day,
    int RetentionPercent,
    int ReviewsCount);
