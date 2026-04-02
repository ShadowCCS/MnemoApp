using System;
using System.Collections.Generic;
using System.Text.Json;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

internal static class RoutingAndSkillResponseParser
{
    public static RoutingAndSkillDecision? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw.Trim());
            var root = doc.RootElement;

            RoutingComplexity complexity = RoutingComplexity.Simple;
            if (root.TryGetProperty("complexity", out var complexityProp) &&
                complexityProp.ValueKind == JsonValueKind.String &&
                string.Equals(complexityProp.GetString(), "reasoning", StringComparison.OrdinalIgnoreCase))
            {
                complexity = RoutingComplexity.Reasoning;
            }

            var skillsList = TryParseSkillsArray(root);
            if (skillsList == null &&
                root.TryGetProperty("skill", out var skillProp) &&
                skillProp.ValueKind == JsonValueKind.String)
            {
                var one = skillProp.GetString();
                if (!string.IsNullOrWhiteSpace(one))
                    skillsList = new List<string> { one.Trim() };
            }

            if (skillsList == null || skillsList.Count == 0)
                return null;

            string? confidence = null;
            if (root.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.String)
                confidence = confProp.GetString();

            string? reason = null;
            if (root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                reason = reasonProp.GetString();

            return new RoutingAndSkillDecision
            {
                Complexity = complexity,
                Skills = skillsList,
                Confidence = confidence,
                Reason = reason
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// New contract: non-empty <c>skills</c> array. Returns null if absent or empty.
    /// </summary>
    private static List<string>? TryParseSkillsArray(JsonElement root)
    {
        if (!root.TryGetProperty("skills", out var skillsProp) || skillsProp.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var el in skillsProp.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String) continue;
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s.Trim());
        }

        return list.Count > 0 ? list : null;
    }
}
