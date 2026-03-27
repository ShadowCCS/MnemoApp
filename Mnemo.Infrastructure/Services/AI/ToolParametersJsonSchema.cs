using System;
using System.Text.Json;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Converts skill <c>tools.json</c> parameter blobs into JSON Schema objects that llama.cpp
/// and OpenAI-compatible servers accept. Legacy format was a flat map of property name → fragment
/// (e.g. <c>{"title":{"type":"string"}}</c>); valid schema requires
/// <c>{"type":"object","properties":{...}}</c>.
/// </summary>
internal static class ToolParametersJsonSchema
{
    /// <summary>
    /// Returns a value suitable for JSON serialization as <c>function.parameters</c>.
    /// </summary>
    public static object Normalize(JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new { type = "object", properties = new { } };

        if (parameters.ValueKind != JsonValueKind.Object)
            return new { type = "object", properties = new { } };

        // Already OpenAI / JSON Schema style: has a "properties" object at root.
        if (parameters.TryGetProperty("properties", out _))
            return JsonSerializer.Deserialize<object>(parameters.GetRawText())!;

        // Root JSON Schema with explicit "type" (object, array, string, …) and no legacy flat keys only.
        if (parameters.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            return JsonSerializer.Deserialize<object>(parameters.GetRawText())!;

        // Legacy: top-level keys are parameter names; values are per-field schema fragments.
        return new
        {
            type = "object",
            properties = JsonSerializer.Deserialize<object>(parameters.GetRawText())
        };
    }
}
