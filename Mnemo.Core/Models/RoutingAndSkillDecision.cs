using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Combined manager decision for chat model routing and skill classification.
/// </summary>
public sealed class RoutingAndSkillDecision
{
    public RoutingComplexity Complexity { get; init; }

    /// <summary>
    /// Ordered skill ids from the router (same user turn). Use <c>["NONE"]</c> for general study chat with no module tools.
    /// For multi-module requests, list distinct ids in execution order, e.g. <c>["Notes", "Mindmap"]</c>.
    /// </summary>
    public IReadOnlyList<string> Skills { get; init; } = new[] { "NONE" };

    /// <summary>First entry of <see cref="Skills"/> (primary module).</summary>
    public string PrimarySkill => Skills.Count > 0 ? Skills[0] : "NONE";

    public string? Confidence { get; init; }
    public string? Reason { get; init; }

    /// <summary>
    /// Distinct non-NONE skill ids for injection.
    /// </summary>
    public IReadOnlyList<string> GetNormalizedSkillIds()
    {
        var list = new List<string>();
        foreach (var s in Skills)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var t = s.Trim();
            if (string.Equals(t, "NONE", StringComparison.OrdinalIgnoreCase)) continue;
            if (list.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(t);
        }

        return list.Count > 0 ? list : new[] { "NONE" };
    }
}
