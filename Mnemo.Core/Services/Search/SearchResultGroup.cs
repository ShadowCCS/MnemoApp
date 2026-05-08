using System.Collections.Generic;

namespace Mnemo.Core.Services.Search;

public sealed class SearchResultGroup
{
    public required string GroupKey { get; init; }
    public required string GroupDisplayName { get; init; }
    public required int GroupOrder { get; init; }
    public SearchResultType ResultType { get; init; } = SearchResultType.Unknown;
    public required IReadOnlyList<SearchResultItem> Items { get; init; }
    public int TotalMatched { get; init; }
    public bool HasMore => TotalMatched > Items.Count;
}
