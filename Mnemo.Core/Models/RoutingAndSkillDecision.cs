namespace Mnemo.Core.Models;

/// <summary>
/// Combined manager decision for chat model routing and skill classification.
/// </summary>
public sealed class RoutingAndSkillDecision
{
    public RoutingComplexity Complexity { get; init; }
    public string Skill { get; init; } = "NONE";
    public string? Confidence { get; init; }
    public string? Reason { get; init; }
}
