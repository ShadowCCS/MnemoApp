using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAIOrchestrator
{
    Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    /// <param name="imageBase64Contents">Optional. For vision (e.g. low-tier model): images to send with the user prompt.</param>
    /// <param name="routingUserMessage">Optional. When set, used only for orchestration routing / complexity (not sent as the chat prompt). Use the latest user turn so routing stays fast as history grows.</param>
    /// <param name="pipelineStatus">Optional. Reports <see cref="ChatPipelineStatusKeys"/> localization keys while routing, loading the model, or before the first token.</param>
    IAsyncEnumerable<string> PromptStreamingAsync(string systemPrompt, string userPrompt, CancellationToken ct = default, IReadOnlyList<string>? imageBase64Contents = null, string? routingUserMessage = null, IProgress<string>? pipelineStatus = null);
    /// <param name="responseJsonSchema">Optional. When set, forwarded to text service so the server forces JSON output (same as mini manager); use e.g. LearningPathJsonSchema.GetSchema().</param>
    Task<Result<string>> PromptWithModelAsync(string modelId, string prompt, CancellationToken ct = default, object? responseJsonSchema = null);

    /// <summary>RAG-enabled prompt (no system prompt).</summary>
    Task<Result<string>> PromptWithContextAsync(string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default);

    /// <param name="responseJsonSchema">Optional. When set, server is asked to return JSON matching this schema (Llama forced output; same mechanism as the manager model).</param>
    Task<Result<string>> PromptWithContextAsync(string systemPrompt, string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default, object? responseJsonSchema = null);

    /// <summary>Starts the low-tier chat model server if not already running, to reduce first-request latency when the user starts typing in chat.</summary>
    Task WarmUpLowTierModelAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the same routing as send-time selection for <paramref name="routingUserMessage"/>, caches the result for a short window
    /// when appropriate, and may start the chosen model server. Skips mid/high warm-up when manager confidence is low for reasoning
    /// (low-tier hardware still prefetches the low model),
    /// and throttles rapid switches between heavy models. Send-time selection accepts the cache when the draft is still close (edit distance).
    /// </summary>
    Task PrefetchRoutingAndWarmupAsync(string routingUserMessage, CancellationToken ct = default);
}


