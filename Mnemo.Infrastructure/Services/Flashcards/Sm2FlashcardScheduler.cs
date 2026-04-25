using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

public sealed class Sm2FlashcardScheduler : IFlashcardScheduler
{
    public FlashcardSchedulingAlgorithm Algorithm => FlashcardSchedulingAlgorithm.Sm2;

    public Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset reviewedAt) =>
        FlashcardScheduling.ApplySm2Grade(card, grade, reviewedAt);

    public string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        FlashcardScheduling.DescribeSm2Interval(card, grade, now);
}
