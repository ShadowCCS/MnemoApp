namespace Mnemo.Core.Models;

/// <summary>
/// A tool invocation requested by the model during generation.
/// </summary>
public sealed record ToolCallRequest(
    /// <summary>Correlation ID from the model (OpenAI tool_calls[].id). Used to pair results back to the call.</summary>
    string Id,
    string Name,
    string ArgumentsJson);
