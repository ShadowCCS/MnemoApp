using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Flashcards;

public sealed class FsrsFlashcardScheduler : IFlashcardScheduler
{
    private readonly FlashcardFsrsParameters _parameters;

    public FsrsFlashcardScheduler(FlashcardFsrsParameters parameters)
    {
        _parameters = parameters;
    }

    public FlashcardSchedulingAlgorithm Algorithm => FlashcardSchedulingAlgorithm.Fsrs;

    public Flashcard ApplyGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset reviewedAt) =>
        FlashcardScheduling.ApplyFsrsGrade(card, grade, reviewedAt, _parameters);

    public string DescribeInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        FlashcardScheduling.DescribeFsrsInterval(card, grade, now, _parameters);
}
