namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// Optional source metadata for traceability back to originating content.
/// </summary>
public sealed record FlashcardSourceInfo(
    string SourceType,
    string SourceId,
    string? DisplayLabel);
