namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Shared scheduling helpers for both runtime prediction and persistence updates.
/// </summary>
public static class FlashcardScheduling
{
    /// <summary>
    /// Returns an updated card snapshot after grading.
    /// </summary>
    public static Flashcard ApplyGrade(
        Flashcard card,
        FlashcardReviewGrade grade,
        DateTimeOffset now,
        FlashcardSchedulingAlgorithm algorithm = FlashcardSchedulingAlgorithm.Fsrs)
    {
        ArgumentNullException.ThrowIfNull(card);

        return algorithm switch
        {
            FlashcardSchedulingAlgorithm.Fsrs => ApplyFsrsGrade(card, grade, now),
            FlashcardSchedulingAlgorithm.Sm2 => ApplySm2Grade(card, grade, now),
            FlashcardSchedulingAlgorithm.Leitner => ApplyLeitnerGrade(card, grade, now),
            _ => ApplyBaselineGrade(card, grade, now)
        };
    }

    /// <summary>
    /// Returns a human-readable interval preview for grade choices.
    /// </summary>
    public static string DescribeInterval(
        Flashcard card,
        FlashcardReviewGrade grade,
        DateTimeOffset now,
        FlashcardSchedulingAlgorithm algorithm = FlashcardSchedulingAlgorithm.Fsrs)
    {
        var next = ApplyGrade(card, grade, now, algorithm).DueDate;
        var delta = next - now;
        if (delta.TotalDays >= 1d)
            return $"{Math.Round(delta.TotalDays, MidpointRounding.AwayFromZero):0}d";
        if (delta.TotalHours >= 1d)
            return $"{Math.Round(delta.TotalHours, MidpointRounding.AwayFromZero):0}h";
        return $"{Math.Max(1, Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero)):0}m";
    }

    public static Flashcard ApplyBaselineGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now)
    {
        var baseStability = Math.Max(card.Stability ?? 1d, 0.2d);
        var baseDifficulty = Clamp(card.Difficulty ?? 5d, 1d, 10d);
        var nextDue = CalculateBaselineNextDue(now, grade, baseStability);

        return card with
        {
            DueDate = nextDue,
            Stability = grade switch
            {
                FlashcardReviewGrade.Again => Math.Max(0.25d, baseStability * 0.8d),
                FlashcardReviewGrade.Hard => baseStability * 1.05d,
                FlashcardReviewGrade.Good => baseStability * 1.2d,
                FlashcardReviewGrade.Easy => baseStability * 1.45d,
                _ => baseStability
            },
            Difficulty = Clamp(baseDifficulty + grade switch
            {
                FlashcardReviewGrade.Again => 0.35d,
                FlashcardReviewGrade.Hard => 0.15d,
                FlashcardReviewGrade.Good => -0.1d,
                FlashcardReviewGrade.Easy => -0.25d,
                _ => 0d
            }, 1d, 10d),
            Retrievability = grade switch
            {
                FlashcardReviewGrade.Again => 0.45d,
                FlashcardReviewGrade.Hard => 0.7d,
                FlashcardReviewGrade.Good => 0.85d,
                FlashcardReviewGrade.Easy => 0.95d,
                _ => 0.8d
            },
            ReviewCount = (card.ReviewCount ?? 0) + 1,
            LapseCount = (card.LapseCount ?? 0) + (grade == FlashcardReviewGrade.Again ? 1 : 0)
        };
    }

    public static Flashcard ApplyFsrsGrade(
        Flashcard card,
        FlashcardReviewGrade grade,
        DateTimeOffset now,
        FlashcardFsrsParameters? parameters = null)
    {
        var cfg = parameters ?? FlashcardFsrsParameters.Default;
        var weights = cfg.Weights;
        if (weights.Length < 21)
            throw new ArgumentException("FSRS requires 21 weights.", nameof(parameters));

        var state = GetFsrsState(card);
        var elapsedDays = state == FlashcardFsrsState.New
            ? 0d
            : Math.Max(0d, (now - (card.LastReviewedAt ?? card.DueDate)).TotalDays);
        var reps = Math.Max(0, card.ReviewCount ?? 0);

        var logRetrievability = state == FlashcardFsrsState.New
            ? 1d
            : Forgetting(elapsedDays, Math.Max(0.1d, card.Stability ?? 0.1d), weights);

        var stability = Math.Max(0.1d, card.Stability ?? InitialStability(grade, weights));
        var difficulty = Clamp(card.Difficulty ?? InitialDifficulty(grade, weights), 1d, 10d);
        var nextState = state;
        DateTimeOffset dueDate;

        if (state == FlashcardFsrsState.New)
        {
            stability = InitialStability(grade, weights);
            difficulty = InitialDifficulty(grade, weights);
            dueDate = grade switch
            {
                FlashcardReviewGrade.Again => now.AddMinutes(1),
                FlashcardReviewGrade.Hard => now.AddMinutes(5),
                FlashcardReviewGrade.Good => now.AddMinutes(10),
                _ => now.AddDays(NextInterval(stability, cfg.DesiredRetention, weights))
            };
            nextState = grade == FlashcardReviewGrade.Easy
                ? FlashcardFsrsState.Review
                : FlashcardFsrsState.Learning;
        }
        else
        {
            stability = elapsedDays < 1d
                ? ShortTermStability(stability, grade, weights)
                : grade == FlashcardReviewGrade.Again
                    ? ForgetStability(difficulty, stability, logRetrievability, weights)
                    : RecallStability(difficulty, stability, logRetrievability, grade, weights);

            difficulty = NextDifficulty(difficulty, grade, weights);

            if (grade == FlashcardReviewGrade.Again)
            {
                nextState = FlashcardFsrsState.Relearning;
                dueDate = now.AddMinutes(10);
            }
            else
            {
                nextState = FlashcardFsrsState.Review;
                dueDate = now.AddDays(NextInterval(stability, cfg.DesiredRetention, weights));
            }
        }

        return card with
        {
            DueDate = dueDate,
            Stability = Math.Max(0.1d, stability),
            Difficulty = Clamp(difficulty, 1d, 10d),
            Retrievability = logRetrievability,
            ReviewCount = reps + 1,
            LapseCount = (card.LapseCount ?? 0) + (grade == FlashcardReviewGrade.Again ? 1 : 0),
            LeitnerBox = null,
            LastReviewedAt = now,
            FsrsState = nextState
        };
    }

    private static FlashcardFsrsState GetFsrsState(Flashcard card)
    {
        if (card.FsrsState is { } state)
            return state;

        return (card.ReviewCount ?? 0) <= 0
            ? FlashcardFsrsState.New
            : FlashcardFsrsState.Review;
    }

    public static string DescribeBaselineInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        DescribeInterval(card, grade, now, FlashcardSchedulingAlgorithm.Baseline);

    public static string DescribeFsrsInterval(
        Flashcard card,
        FlashcardReviewGrade grade,
        DateTimeOffset now,
        FlashcardFsrsParameters? parameters = null)
    {
        var next = ApplyFsrsGrade(card, grade, now, parameters).DueDate;
        var delta = next - now;
        if (delta.TotalDays >= 1d)
            return $"{Math.Round(delta.TotalDays, MidpointRounding.AwayFromZero):0}d";
        if (delta.TotalHours >= 1d)
            return $"{Math.Round(delta.TotalHours, MidpointRounding.AwayFromZero):0}h";
        return $"{Math.Max(1, Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero)):0}m";
    }

    public static Flashcard ApplySm2Grade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        ApplySm2(card, grade, now);

    public static Flashcard ApplyLeitnerGrade(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        ApplyLeitner(card, grade, now);

    public static string DescribeSm2Interval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        DescribeInterval(card, grade, now, FlashcardSchedulingAlgorithm.Sm2);

    public static string DescribeLeitnerInterval(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now) =>
        DescribeInterval(card, grade, now, FlashcardSchedulingAlgorithm.Leitner);

    private static int NextInterval(double stability, double desiredRetention, double[] weights)
    {
        var decay = -(weights[20] + 0.5d);
        var factor = Math.Pow(0.9d, 1d / decay) - 1d;
        var interval = stability / factor * (Math.Pow(desiredRetention, 1d / decay) - 1d);
        return Math.Max(1, (int)Math.Round(interval, MidpointRounding.AwayFromZero));
    }

    private static double Forgetting(double elapsedDays, double stability, double[] weights)
    {
        var decay = -(weights[20] + 0.5d);
        var factor = Math.Pow(0.9d, 1d / decay) - 1d;
        return Math.Pow(1d + factor * elapsedDays / Math.Max(0.1d, stability), decay);
    }

    private static double InitialStability(FlashcardReviewGrade grade, double[] weights) =>
        Math.Max(weights[(int)grade - 1], 0.1d);

    private static double InitialDifficulty(FlashcardReviewGrade grade, double[] weights)
    {
        var d = weights[4] - Math.Exp(weights[5] * ((int)grade - 1)) + 1d;
        return Clamp(d, 1d, 10d);
    }

    private static double NextDifficulty(double difficulty, FlashcardReviewGrade grade, double[] weights)
    {
        var delta = -weights[6] * ((int)grade - 3);
        var raw = difficulty + delta * (10d - difficulty) / 9d;
        raw = weights[7] * InitialDifficulty(FlashcardReviewGrade.Easy, weights) + (1d - weights[7]) * raw;
        return Clamp(raw, 1d, 10d);
    }

    private static double ShortTermStability(double stability, FlashcardReviewGrade grade, double[] weights)
    {
        var updated = stability * Math.Exp(weights[17] * ((int)grade - 3 + weights[18]));
        return Math.Max(updated, 0.1d);
    }

    private static double RecallStability(
        double difficulty,
        double stability,
        double retrievability,
        FlashcardReviewGrade grade,
        double[] weights)
    {
        var hardPenalty = grade == FlashcardReviewGrade.Hard ? weights[15] : 1d;
        var easyBonus = grade == FlashcardReviewGrade.Easy ? weights[16] : 1d;
        var updated = stability * (
            Math.Exp(weights[8]) *
            (11d - difficulty) *
            Math.Pow(stability, -weights[9]) *
            (Math.Exp((1d - retrievability) * weights[10]) - 1d) *
            hardPenalty * easyBonus + 1d);
        return Math.Max(updated, 0.1d);
    }

    private static double ForgetStability(double difficulty, double stability, double retrievability, double[] weights)
    {
        var updated = weights[11]
            * Math.Pow(difficulty, -weights[12])
            * (Math.Pow(stability + 1d, weights[13]) - 1d)
            * Math.Exp(weights[14] * (1d - retrievability));
        return Math.Max(updated, 0.1d);
    }

    private static Flashcard ApplySm2(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now)
    {
        var quality = grade switch
        {
            FlashcardReviewGrade.Again => 1,
            FlashcardReviewGrade.Hard => 3,
            FlashcardReviewGrade.Good => 4,
            FlashcardReviewGrade.Easy => 5,
            _ => 3
        };

        var easeFactor = Clamp(card.Difficulty ?? 2.5d, 1.3d, 3.2d);
        var priorReviewCount = Math.Max(0, card.ReviewCount ?? 0);
        var nextReviewCount = quality < 3 ? 0 : priorReviewCount + 1;

        var intervalDays = quality < 3
            ? 1d / 144d
            : nextReviewCount switch
            {
                1 => 1d,
                2 => 6d,
                _ => Math.Max(1d, Math.Round((card.Stability ?? 6d) * easeFactor, MidpointRounding.AwayFromZero))
            };

        easeFactor += 0.1d - (5 - quality) * (0.08d + (5 - quality) * 0.02d);
        easeFactor = Clamp(easeFactor, 1.3d, 3.2d);

        return card with
        {
            DueDate = now.AddDays(intervalDays),
            Stability = intervalDays,
            Difficulty = easeFactor,
            Retrievability = quality < 3 ? 0.4d : 0.9d,
            ReviewCount = nextReviewCount,
            LapseCount = (card.LapseCount ?? 0) + (quality < 3 ? 1 : 0),
            LeitnerBox = null
        };
    }

    private static Flashcard ApplyLeitner(Flashcard card, FlashcardReviewGrade grade, DateTimeOffset now)
    {
        var box = Math.Clamp(card.LeitnerBox ?? 1, 1, 7);
        box = grade switch
        {
            FlashcardReviewGrade.Again => 1,
            FlashcardReviewGrade.Hard => Math.Max(1, box - 1),
            FlashcardReviewGrade.Good => Math.Min(7, box + 1),
            FlashcardReviewGrade.Easy => Math.Min(7, box + 2),
            _ => box
        };

        var intervalDays = box switch
        {
            1 => 0.01d,
            2 => 1d,
            3 => 2d,
            4 => 4d,
            5 => 8d,
            6 => 16d,
            _ => 32d
        };

        return card with
        {
            DueDate = now.AddDays(intervalDays),
            Stability = intervalDays,
            Difficulty = 8d - box,
            Retrievability = grade == FlashcardReviewGrade.Again ? 0.35d : 0.85d,
            ReviewCount = (card.ReviewCount ?? 0) + 1,
            LapseCount = (card.LapseCount ?? 0) + (grade == FlashcardReviewGrade.Again ? 1 : 0),
            LeitnerBox = box
        };
    }

    private static DateTimeOffset CalculateBaselineNextDue(DateTimeOffset now, FlashcardReviewGrade grade, double stability)
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
