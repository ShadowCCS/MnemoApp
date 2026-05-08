using System.Collections.Generic;

namespace Mnemo.Core.Services.Search;

public sealed class FlashcardSearchMetadata
{
    public string? DeckId { get; init; }
    public string? DeckTitle { get; init; }
    public string? FrontText { get; init; }
    public string? BackText { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
