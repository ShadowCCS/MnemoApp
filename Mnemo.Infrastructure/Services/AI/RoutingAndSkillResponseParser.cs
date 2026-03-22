using System;
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

            if (!root.TryGetProperty("complexity", out var complexityProp) || complexityProp.ValueKind != JsonValueKind.String)
                return null;

            if (!root.TryGetProperty("skill", out var skillProp) || skillProp.ValueKind != JsonValueKind.String)
                return null;

            var complexity = string.Equals(complexityProp.GetString(), "reasoning", StringComparison.OrdinalIgnoreCase)
                ? RoutingComplexity.Reasoning
                : RoutingComplexity.Simple;

            var skill = skillProp.GetString();
            if (string.IsNullOrWhiteSpace(skill))
                skill = "NONE";

            string? confidence = null;
            if (root.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.String)
                confidence = confProp.GetString();

            string? reason = null;
            if (root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                reason = reasonProp.GetString();

            return new RoutingAndSkillDecision
            {
                Complexity = complexity,
                Skill = skill!,
                Confidence = confidence,
                Reason = reason
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
