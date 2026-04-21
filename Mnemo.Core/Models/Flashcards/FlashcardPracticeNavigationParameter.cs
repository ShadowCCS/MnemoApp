namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Navigation payload for starting practice on a deck with a given session configuration.
/// </summary>
public sealed record FlashcardPracticeNavigationParameter(string DeckId, FlashcardSessionConfig SessionConfig);
