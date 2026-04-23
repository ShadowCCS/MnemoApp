using Mnemo.Core.Models;

namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// A single flashcard belonging to a deck.
/// </summary>
public sealed record Flashcard(
    string Id,
    string DeckId,
    string Front,
    string Back,
    FlashcardType Type,
    IReadOnlyList<string> Tags,
    DateTimeOffset DueDate,
    double? Stability,
    double? Difficulty,
    double? Retrievability,
    FlashcardSourceInfo? SourceInfo = null,
    IReadOnlyList<Block>? FrontBlocks = null,
    IReadOnlyList<Block>? BackBlocks = null);
