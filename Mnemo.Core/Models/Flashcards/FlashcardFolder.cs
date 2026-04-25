namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// A folder used to group decks in the library.
/// </summary>
public sealed record FlashcardFolder(
    string Id,
    string Name,
    string? ParentId,
    int Order);
