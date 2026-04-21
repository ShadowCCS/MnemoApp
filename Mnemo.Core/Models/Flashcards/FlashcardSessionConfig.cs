namespace Mnemo.Core.Models.Flashcards;

/// <summary>
/// User-selected options when starting a practice session.
/// </summary>
public sealed record FlashcardSessionConfig(
    FlashcardSessionType SessionType,
    string DeckId,
    int? CardCount,
    int? TimeLimitMinutes,
    bool Shuffle,
    FlashcardTestGradingMode? TestGradingMode);
