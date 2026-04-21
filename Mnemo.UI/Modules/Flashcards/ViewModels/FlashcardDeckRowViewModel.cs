using System;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

/// <summary>
/// One row in the flashcard library deck list.
/// </summary>
public sealed class FlashcardDeckRowViewModel
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int DueCount { get; init; }

    public int TotalCards { get; init; }

    public int RetentionScore { get; init; }

    public string? FolderId { get; init; }

    /// <summary>Localized "n due" when <see cref="DueCount"/> &gt; 0; otherwise empty.</summary>
    public string DueBadgeText { get; init; } = string.Empty;

    public bool ShowDueBadge => DueCount > 0;

    /// <summary>Localized "n cards".</summary>
    public string CardCountLine { get; init; } = string.Empty;

    /// <summary>Localized last studied or never studied.</summary>
    public string LastStudiedLine { get; init; } = string.Empty;

    /// <summary>Retention for progress bar (0–100).</summary>
    public int RetentionPercent => Math.Clamp(RetentionScore, 0, 100);

    /// <summary>Compact line for deck cards (due count and retention).</summary>
    public string SummaryLine => $"{DueCount} · {RetentionScore}%";
}
