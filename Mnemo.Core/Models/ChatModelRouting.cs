namespace Mnemo.Core.Models;

/// <summary>User-selected model tier for chat: follow the manager router or force low-tier vs reasoning path.</summary>
public static class ChatModelRouting
{
    public const string Auto = "Auto";
    public const string Simple = "Simple";
    public const string Reasoning = "Reasoning";

    /// <summary>Maps UI/persisted values to Auto, Simple, or Reasoning.</summary>
    public static string NormalizeModelRoutingMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return Auto;
        if (string.Equals(mode, Simple, StringComparison.OrdinalIgnoreCase)) return Simple;
        if (string.Equals(mode, Reasoning, StringComparison.OrdinalIgnoreCase)) return Reasoning;
        return Auto;
    }

    /// <summary>
    /// When mode is Simple or Reasoning, replaces <see cref="RoutingAndSkillDecision.Complexity"/> while keeping skills and metadata from the manager.
    /// </summary>
    public static RoutingAndSkillDecision ApplyComplexityOverride(RoutingAndSkillDecision decision, string? mode)
    {
        var m = NormalizeModelRoutingMode(mode);
        if (m == Auto) return decision;
        var complexity = m == Reasoning ? RoutingComplexity.Reasoning : RoutingComplexity.Simple;
        return new RoutingAndSkillDecision
        {
            Complexity = complexity,
            Skills = decision.Skills,
            Confidence = decision.Confidence,
            Reason = decision.Reason
        };
    }
}
