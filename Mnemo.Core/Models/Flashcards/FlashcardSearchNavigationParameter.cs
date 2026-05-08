namespace Mnemo.Core.Models.Flashcards;

public sealed record FlashcardSearchNavigationParameter(
    string DeckId,
    string? SearchQuery = null,
    string? CardId = null);
