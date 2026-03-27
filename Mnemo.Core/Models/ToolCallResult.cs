namespace Mnemo.Core.Models;

/// <summary>
/// The outcome of executing a tool call, to be fed back to the model as a tool-result message.
/// </summary>
public sealed record ToolCallResult(
    /// <summary>Must match <see cref="ToolCallRequest.Id"/> so the model can correlate results to calls.</summary>
    string ToolCallId,
    string Name,
    string Content);
