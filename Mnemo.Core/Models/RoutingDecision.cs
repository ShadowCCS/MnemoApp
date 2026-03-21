namespace Mnemo.Core.Models;

/// <summary>
/// Parsed routing result from the background 0.6B orchestration model.
/// </summary>
public sealed class RoutingDecision
{
    public RoutingComplexity Complexity { get; init; }
    public string? Confidence { get; init; }
    public string? Reason { get; init; }
}
