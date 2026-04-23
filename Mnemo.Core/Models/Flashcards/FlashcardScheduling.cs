namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Shared scheduling helpers for both runtime prediction and persistence updates.
/// </summary>
public static class FlashcardScheduling
{
    /// <summary>
    /// Returns an updated card snapshot after grading.
    /// </summary>
    public static Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);

        var baseStability = Math.Max(card.Stability ?? 1d, 0.2d);
        var baseDifficulty = Clamp(card.Difficulty ?? 5d, 1d, 10d);
        var nextDue = CalculateNextDue(now, grade, baseStability);
        var updatedStability = grade switch
        {
            FlashcardReviewGrade.Again => Math.Max(0.25d, baseStability * 0.8d),
            FlashcardReviewGrade.Hard => baseStability * 1.05d,
            FlashcardReviewGrade.Good => baseStability * 1.2d,
            FlashcardReviewGrade.Easy => baseStability * 1.45d,
            _ => baseStability
        };
        var updatedDifficulty = Clamp(baseDifficulty + grade switch
        {
            FlashcardReviewGrade.Again => 0.35d,
            FlashcardReviewGrade.Hard => 0.15d,
            FlashcardReviewGrade.Good => -0.1d,
            FlashcardReviewGrade.Easy => -0.25d,
            _ => 0d
        }, 1d, 10d);
        var retrievability = grade switch
        {
            FlashcardReviewGrade.Again => 0.45d,
            FlashcardReviewGrade.Hard => 0.7d,
            FlashcardReviewGrade.Good => 0.85d,
            FlashcardReviewGrade.Easy => 0.95d,
            _ => 0.8d
        };

        return card with
        {
            DueDate = nextDue,
            Stability = updatedStability,
            Difficulty = updatedDifficulty,
            Retrievability = retrievability
        };
    }

    /// <summary>
    /// Returns a human-readable interval preview for grade choices.
    /// </summary>
    public static string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now)
    {
        var next = CalculateNextDue(now, grade, Math.Max(card.Stability ?? 1d, 0.2d));
        var delta = next - now;
        if (delta.TotalDays >= 1d)
            return $"{Math.Round(delta.TotalDays, MidpointRounding.AwayFromZero):0}d";
        if (delta.TotalHours >= 1d)
            return $"{Math.Round(delta.TotalHours, MidpointRounding.AwayFromZero):0}h";
        return $"{Math.Max(1, Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero)):0}m";
    }

    private static DateTimeOffset CalculateNextDue(DateTimeOffset now, FlashcardReviewGrade grade, double stability)
    {
        var minutes = grade switch
        {
            FlashcardReviewGrade.Again => 10d,
            FlashcardReviewGrade.Hard => Math.Max(60d, stability * 24d * 60d * 0.8d),
            FlashcardReviewGrade.Good => Math.Max(120d, stability * 24d * 60d * 1.8d),
            FlashcardReviewGrade.Easy => Math.Max(240d, stability * 24d * 60d * 3d),
            _ => 60d
        };
        return now.AddMinutes(minutes);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
