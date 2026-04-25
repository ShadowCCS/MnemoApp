using Mnemo.Core.Models.Flashcards;

namespace Mnemo.Infrastructure.Tests.Flashcards;

public sealed class FlashcardSchedulingTests
{
    [Theory]
    [InlineData(FlashcardSchedulingAlgorithm.Fsrs)]
    [InlineData(FlashcardSchedulingAlgorithm.Sm2)]
    [InlineData(FlashcardSchedulingAlgorithm.Leitner)]
    [InlineData(FlashcardSchedulingAlgorithm.Baseline)]
    public void ApplyGrade_UpdatesDueDate_ForAllAlgorithms(FlashcardSchedulingAlgorithm algorithm)
    {
        var now = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero);
        var card = new Flashcard(
            "c1",
            "d1",
            "front",
            "back",
            FlashcardType.Classic,
            Array.Empty<string>(),
            now,
            2.5d,
            5d,
            0.8d);

        var updated = FlashcardScheduling.ApplyGrade(card, FlashcardReviewGrade.Good, now, algorithm);

        Assert.True(updated.DueDate > now);
        Assert.True(updated.ReviewCount >= 1);
    }

    [Fact]
    public void ApplyGrade_Leitner_TracksBoxProgression()
    {
        var now = DateTimeOffset.UtcNow;
        var card = new Flashcard(
            "c1",
            "d1",
            "front",
            "back",
            FlashcardType.Classic,
            Array.Empty<string>(),
            now,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            1);

        var good = FlashcardScheduling.ApplyGrade(card, FlashcardReviewGrade.Good, now, FlashcardSchedulingAlgorithm.Leitner);
        var again = FlashcardScheduling.ApplyGrade(good, FlashcardReviewGrade.Again, now, FlashcardSchedulingAlgorithm.Leitner);

        Assert.Equal(2, good.LeitnerBox);
        Assert.Equal(1, again.LeitnerBox);
    }

    [Fact]
    public void ApplyGrade_Fsrs_NewCard_UsesLearningStepThenGraduates()
    {
        var now = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero);
        var card = new Flashcard(
            "c1",
            "d1",
            "front",
            "back",
            FlashcardType.Classic,
            Array.Empty<string>(),
            now,
            null,
            null,
            null);

        var firstGood = FlashcardScheduling.ApplyGrade(card, FlashcardReviewGrade.Good, now, FlashcardSchedulingAlgorithm.Fsrs);
        var secondGood = FlashcardScheduling.ApplyGrade(firstGood, FlashcardReviewGrade.Good, now.AddMinutes(12), FlashcardSchedulingAlgorithm.Fsrs);

        Assert.Equal(FlashcardFsrsState.Learning, firstGood.FsrsState);
        Assert.True((firstGood.DueDate - now).TotalMinutes >= 9.9d);
        Assert.Equal(FlashcardFsrsState.Review, secondGood.FsrsState);
        Assert.True((secondGood.DueDate - now.AddMinutes(12)).TotalDays >= 1d);
    }
}
