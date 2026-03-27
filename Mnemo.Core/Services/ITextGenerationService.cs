using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ITextGenerationService : IDisposable
{
    /// <param name="responseJsonSchema">Optional. When set, request is sent with response_format so the server (e.g. Llama) forces JSON output matching this schema; same mechanism as the manager model.</param>
    Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct, object? responseJsonSchema = null);
    /// <param name="imageBase64Contents">Optional. For vision models: list of image bytes as base64 strings (without data URL prefix).</param>
    IAsyncEnumerable<string> GenerateStreamingAsync(AIModelManifest manifest, string prompt, CancellationToken ct, IReadOnlyList<string>? imageBase64Contents = null);
    /// <summary>
    /// Tool-aware streaming generation. Yields <see cref="StreamChunk.Content"/> tokens during normal generation
    /// and <see cref="StreamChunk.ToolCall"/> when the model requests a tool invocation.
    /// </summary>
    /// <param name="messages">Full conversation history in OpenAI message format (system, user, assistant, tool).</param>
    /// <param name="tools">Tool schemas to advertise to the model. Empty list means no tool calling.</param>
    IAsyncEnumerable<StreamChunk> GenerateStreamingWithToolsAsync(AIModelManifest manifest, IReadOnlyList<object> messages, IReadOnlyList<SkillToolDefinition> tools, CancellationToken ct);
    void UnloadModel(string modelId);
}


