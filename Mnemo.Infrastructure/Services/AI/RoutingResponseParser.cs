using System;
using System.Text.Json;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

internal static class RoutingResponseParser
{
    public static RoutingDecision? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw.Trim());
            var root = doc.RootElement;
            if (!root.TryGetProperty("complexity", out var complexityProp) || complexityProp.ValueKind != JsonValueKind.String)
                return null;

            var complexityStr = complexityProp.GetString() ?? "";
            var complexity = complexityStr.Equals("reasoning", StringComparison.OrdinalIgnoreCase)
                ? RoutingComplexity.Reasoning
                : RoutingComplexity.Simple;

            string? confidence = null;
            if (root.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.String)
                confidence = confProp.GetString();

            string? reason = null;
            if (root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                reason = reasonProp.GetString();

            return new RoutingDecision
            {
                Complexity = complexity,
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
