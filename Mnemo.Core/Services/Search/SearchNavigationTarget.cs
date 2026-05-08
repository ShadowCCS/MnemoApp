namespace Mnemo.Core.Services.Search;

public sealed class SearchNavigationTarget
{
    public string? Route { get; init; }
    public object? Parameter { get; init; }
    public string? Href { get; init; }
}
