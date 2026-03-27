using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Vertex AI (Gemini) teacher model used for dataset-quality routing and chat when developer switches are enabled.
/// </summary>
public interface ITeacherModelClient
{
    /// <summary>Returns true when credentials resolve and project/location/model are available.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Produces a JSON object with complexity, skill, and optional confidence/reason (same contract as the local manager model).
    /// </summary>
    Task<Result<string>> GenerateRoutingDecisionJsonAsync(string userBlock, CancellationToken ct = default);

    /// <summary>Streaming chat without tool calling (system + user text; optional vision images).</summary>
    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<string>? imageBase64Contents,
        CancellationToken ct);

    /// <summary>Tool-aware streaming using OpenAI-style message objects (same shape as local llama server).</summary>
    IAsyncEnumerable<StreamChunk> StreamChatWithToolsAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<SkillToolDefinition> tools,
        CancellationToken ct);

    /// <summary>Non-streaming completion; optional JSON schema forces structured output (Gemini responseSchema).</summary>
    Task<Result<string>> GenerateTextAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct,
        object? responseJsonSchema = null);
}
