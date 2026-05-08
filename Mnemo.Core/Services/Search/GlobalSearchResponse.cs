using System.Collections.Generic;

namespace Mnemo.Core.Services.Search;

public sealed class GlobalSearchResponse
{
    public required IReadOnlyList<SearchResultItem> BestMatches { get; init; }
    public required IReadOnlyList<SearchResultGroup> Groups { get; init; }
}
