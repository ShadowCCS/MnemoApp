using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

public sealed class BaselineFlashcardScheduler : IFlashcardScheduler
{
    public FlashcardSchedulingAlgorithm Algorithm => FlashcardSchedulingAlgorithm.Baseline;

    public Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset reviewedAt) =>
        FlashcardScheduling.ApplyBaselineGrade(card, grade, reviewedAt);

    public string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        FlashcardScheduling.DescribeBaselineInterval(card, grade, now);
}
