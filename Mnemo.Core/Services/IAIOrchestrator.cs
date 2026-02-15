using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAIOrchestrator
{
    Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    IAsyncEnumerable<string> PromptStreamingAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    /// <param name="responseJsonSchema">Optional. When set, forwarded to text service so the server forces JSON output (same as router); use e.g. LearningPathJsonSchema.GetSchema().</param>
    Task<Result<string>> PromptWithModelAsync(string modelId, string prompt, CancellationToken ct = default, object? responseJsonSchema = null);

    /// <summary>RAG-enabled prompt (no system prompt).</summary>
    Task<Result<string>> PromptWithContextAsync(string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default);

    /// <param name="responseJsonSchema">Optional. When set, server is asked to return JSON matching this schema (Llama forced output; same mechanism as router).</param>
    Task<Result<string>> PromptWithContextAsync(string systemPrompt, string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default, object? responseJsonSchema = null);
}


