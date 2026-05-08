using System;
using System.Threading.Tasks;

namespace Mnemo.Core.Services.Search;

public sealed class SearchResultItem
{
    public required string Id { get; init; }
    public SearchResultType Type { get; init; } = SearchResultType.Unknown;
    public required string ProviderId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Preview { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public double Score { get; init; }
    public string? Href { get; init; }
    public SearchNavigationTarget? NavigationTarget { get; init; }
    public Func<Task>? OnNavigateAsync { get; init; }
    public FlashcardSearchMetadata? Flashcard { get; init; }
    public object? Payload { get; init; }
}
