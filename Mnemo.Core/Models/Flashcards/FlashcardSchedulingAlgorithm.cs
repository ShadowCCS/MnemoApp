namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Scheduling algorithm used to update card due dates and memory state.
/// </summary>
public enum FlashcardSchedulingAlgorithm
{
    Baseline = 0,
    Fsrs = 1,
    Sm2 = 2,
    Leitner = 3
}
