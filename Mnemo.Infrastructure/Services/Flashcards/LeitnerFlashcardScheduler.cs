using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

public sealed class LeitnerFlashcardScheduler : IFlashcardScheduler
{
    public FlashcardSchedulingAlgorithm Algorithm => FlashcardSchedulingAlgorithm.Leitner;

    public Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset reviewedAt) =>
        FlashcardScheduling.ApplyLeitnerGrade(card, grade, reviewedAt);

    public string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        FlashcardScheduling.DescribeLeitnerInterval(card, grade, now);
}
