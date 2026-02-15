namespace Mnemo.Core.Schemas;

/// <summary>
/// JSON schema for Learning Path structured output. Used with Llama server forced JSON (response_format / json_schema).
/// The server converts this to a grammar to constrain output shape; the model does NOT see schema or descriptions.
/// For title length/style rules (e.g. "max 4 words"), state them explicitly in the prompt (see GeneratePathTask).
/// </summary>
public static class LearningPathJsonSchema
{
    private static readonly object CachedSchema = CreateSchema();

    private static object CreateSchema() => new
    {
        type = "object",
        required = new[] { "title", "description", "units" },
        properties = new
        {
            title = new { type = "string", maxLength = 50, description = "Title for learning path" },
            description = new { type = "string", description = "Brief overview of the learning path" },
            units = new
            {
                type = "array",
                minItems = 1,
                description = "List of units in the learning path",
                items = new
                {
                    type = "object",
                    required = new[] { "order", "title", "goal", "allocated_material", "generation_hints" },
                    properties = new
                    {
                        order = new { type = "integer", minimum = 1, description = "Sequence order of the unit" },
                        title = new { type = "string", maxLength = 60, description = "Title for the unit" },
                        goal = new { type = "string", description = "What the learner will achieve in this unit" },
                        allocated_material = new
                        {
                            type = "object",
                            required = new[] { "chunk_ids", "summary" },
                            properties = new
                            {
                                chunk_ids = new { type = "array", items = new { type = "string" }, description = "IDs of source material used" },
                                summary = new { type = "string", description = "Summary of source material for this unit" }
                            }
                        },
                        generation_hints = new
                        {
                            type = "object",
                            required = new[] { "focus", "avoid", "prerequisites" },
                            properties = new
                            {
                                focus = new { type = "array", items = new { type = "string" }, description = "Key concepts to focus on" },
                                avoid = new { type = "array", items = new { type = "string" }, description = "Topics to avoid" },
                                prerequisites = new { type = "array", items = new { type = "string" }, description = "Concepts required from previous units" }
                            }
                        }
                    }
                }
            }
        }
    };

    /// <summary>
    /// Returns the JSON schema object for learning path generation. Pass to the orchestrator so the text service sends it as response_format; the server then constrains output to valid JSON matching this schema.
    /// </summary>
    public static object GetSchema() => CachedSchema;
}
