using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAIOrchestrator
{
    Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<Result<RoutingAndSkillDecision>> AnalyzeMessageAsync(
        string userMessage,
        CancellationToken ct = default,
        IProgress<string>? pipelineStatus = null,
        string? conversationRoutingKey = null);
    /// <param name="systemPrompt">Base system prompt only; skill context is composed inside the orchestrator (do not pre-compose).</param>
    /// <param name="imageBase64Contents">Optional. For vision (e.g. low-tier model): images to send with the user prompt.</param>
    /// <param name="routingUserMessage">Optional. When set, used only for orchestration routing / complexity (not sent as the chat prompt). Use the latest user turn so routing stays fast as history grows.</param>
    /// <param name="pipelineStatus">Optional. Reports <see cref="ChatPipelineStatusKeys"/> localization keys while routing, loading the model, or before the first token.</param>
    /// <param name="precomputedDecision">Optional. When provided, avoids an additional manager call and reuses this routing/skill decision for model selection.</param>
    IAsyncEnumerable<string> PromptStreamingAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default,
        IReadOnlyList<string>? imageBase64Contents = null,
        string? routingUserMessage = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null,
        Action<ChatDatasetToolCall>? onToolCall = null);

    /// <summary>
    /// Streaming generation with real multi-turn conversation history. Sends proper OpenAI-format
    /// system/user/assistant message objects instead of a flat text blob, which improves multi-turn
    /// reasoning quality on chat-tuned models.
    /// </summary>
    /// <param name="systemPrompt">Base system prompt only. The implementation composes skill context once via <see cref="ISkillSystemPromptComposer"/>—do not pass an already-composed prompt or skill text will duplicate.</param>
    /// <param name="history">Prior turns (oldest first, excluding the current user message).</param>
    /// <param name="userMessage">The latest user message (will become the final user turn).</param>
    /// <param name="imageBase64Contents">Optional. For vision: images to send with the user message.</param>
    /// <param name="pipelineStatus">Optional. Reports <see cref="ChatPipelineStatusKeys"/> localization keys while routing or loading.</param>
    /// <param name="precomputedDecision">Optional. Reuses a previously computed routing/skill decision.</param>
    IAsyncEnumerable<string> PromptStreamingWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        CancellationToken ct = default,
        IReadOnlyList<string>? imageBase64Contents = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null,
        Action<ChatDatasetToolCall>? onToolCall = null);
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


