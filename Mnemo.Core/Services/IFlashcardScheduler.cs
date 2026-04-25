using Mnemo.Core.Models.Flashcards;

namespace Mnemo.Core.Services;

/// <summary>
/// Strategy interface for scheduling flashcard reviews.
/// </summary>
public interface IFlashcardScheduler
{
    FlashcardSchedulingAlgorithm Algorithm { get; }

    Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset reviewedAt);

    string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now);
}
