namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// User-facing and model parameters used by the FSRS scheduler.
/// </summary>
public sealed record FlashcardFsrsParameters(
    double DesiredRetention,
    double[] Weights)
{
    public static FlashcardFsrsParameters Default { get; } = new(
        0.9d,
        new[]
        {
            0.40255, 1.18385, 3.1262, 15.4722, 7.2102, 0.5316, 1.0651,
            0.0589, 1.5330, 0.1544, 1.0071, 1.9395, 0.1100, 0.2900,
            2.2700, 0.1500, 2.9898, 0.5100, 0.3400, 0.1300, 0.0
        });
}
