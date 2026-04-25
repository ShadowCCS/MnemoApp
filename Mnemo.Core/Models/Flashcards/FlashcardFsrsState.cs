namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// FSRS learning state for a flashcard.
/// </summary>
public enum FlashcardFsrsState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3
}
