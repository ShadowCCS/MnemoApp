namespace Mnemo.Core.Schemas;

/// <summary>
/// Documents the expected routing JSON shape. Routing uses prompt-trained output only (no response_format).
/// </summary>
public static class RoutingJsonSchema
{
    private static readonly object CachedSchema = CreateSchema();

    private static object CreateSchema() => new
    {
        type = "object",
        required = new[] { "complexity", "confidence", "reason" },
        additionalProperties = false,
        properties = new
        {
            complexity = new
            {
                type = "string",
                @enum = new[] { "simple", "reasoning" }
            },
            confidence = new
            {
                type = "string",
                @enum = new[] { "low", "medium", "high" }
            },
            reason = new { type = "string" }
        }
    };

    /// <summary>Returns the schema object (e.g. tests, docs, or optional constrained generation).</summary>
    public static object GetSchema() => CachedSchema;
}
