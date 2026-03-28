using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

/// <summary>
/// Shared streaming chat logic used by the Chat module and Right Sidebar assistant.
/// </summary>
public static class ChatStreamingHelper
{
    /// <summary>System prompt for General mode: helpful assistant (lean; skill fragments + fine-tuning carry product detail).</summary>
    public const string GeneralSystemPrompt = @"You are Mnemo's in-app assistant.

- Answer clearly and concisely. Use Markdown. Default to English unless the user asks otherwise.
- Do not invent app UI, settings, or features. If something is uncertain, say so briefly or ask one focused question.
- Pure study or subject questions: answer directly—no need to mention the app unless relevant.
- When tools are available, use them to read or change user data instead of only describing what you would do.";

    /// <summary>System prompt for Explainer mode: teaching (lean base; same guardrails as General).</summary>
    public const string ExplainerSystemPrompt = @"You are Mnemo's in-app explainer: teach and clarify.

- Match depth to the question (short when enough; steps, examples, or tables when it helps). Markdown; LaTeX for math when useful.
- Do not invent app-specific details. If unsure, say so or ask one short clarifying question.
- Default to English unless the user asks otherwise.";

    /// <summary>Default system prompt (General). Kept for backward compatibility.</summary>
    public static string DefaultSystemPrompt => GeneralSystemPrompt;

    /// <summary>Returns the system prompt for the given assistant mode (e.g. ""General"", ""Explainer"").</summary>
    public static string GetSystemPromptForMode(string mode)
    {
        return string.Equals(mode, "Explainer", StringComparison.OrdinalIgnoreCase)
            ? ExplainerSystemPrompt
            : GeneralSystemPrompt;
    }

    /// <summary>Delay between UI reveal steps while streaming (smooth display, not network pacing) — matches <see cref="ChatStreamingDisplayOptions.Balanced"/>.</summary>
    public const int StreamingDisplayTickMs = 22;

    /// <summary>Maximum characters revealed per tick toward the buffered response — matches <see cref="ChatStreamingDisplayOptions.Balanced"/>.</summary>
    public const int StreamingCharsPerTick = 6;

    /// <summary>Max number of recent messages to include in context (conversation window).</summary>
    public const int MaxContextMessageCount = 11;

    /// <summary>
    /// Builds a structured conversation history from recent messages for multi-turn prompting.
    /// Returns turns oldest-first, excluding the current placeholder (empty assistant) message.
    /// </summary>
    /// <param name="messages">All messages (newest at end).</param>
    /// <param name="excludeMessage">Optional message to exclude (e.g. the placeholder AI message being filled).</param>
    /// <param name="isUser">Predicate: true if the message is from the user.</param>
    /// <param name="getContent">Selector for message content.</param>
    /// <param name="excludeLastUserTurn">
    /// When true, drops the last message if it is a user message. Use with
    /// <see cref="IAIOrchestrator.PromptStreamingWithHistoryAsync"/> which appends <c>userMessage</c> separately—otherwise the latest user turn appears twice.
    /// </param>
    public static IReadOnlyList<ConversationTurn> BuildConversationHistory<T>(
        IList<T> messages,
        T? excludeMessage,
        Func<T, bool> isUser,
        Func<T, string> getContent,
        bool excludeLastUserTurn = false)
    {
        var recent = messages
            .TakeLast(MaxContextMessageCount)
            .Where(m => !ReferenceEquals(m, excludeMessage))
            .ToList();

        if (excludeLastUserTurn && recent.Count > 0 && isUser(recent[^1]))
            recent.RemoveAt(recent.Count - 1);

        if (recent.Count == 0) return Array.Empty<ConversationTurn>();

        return recent
            .Select(m => new ConversationTurn(
                isUser(m) ? ConversationRole.User : ConversationRole.Assistant,
                getContent(m)))
            .ToList();
    }

    /// <summary>
    /// Flat transcript for dataset logging after a turn finishes. Includes every message in the window,
    /// including the assistant reply for the current turn (pass <paramref name="excludeMessage"/> as null).
    /// </summary>
    public static string? BuildDatasetConversationContextString<T>(
        IList<T> messages,
        Func<T, bool> isUser,
        Func<T, string> getContent)
    {
        // No message instance equals default(T), so nothing is excluded (include full transcript for this turn).
        var turns = BuildConversationHistory(messages, default!, isUser, getContent);
        if (turns.Count == 0) return null;
        return "Conversation transcript:\n" + string.Join("\n",
            turns.Select(t => $"{(t.Role == ConversationRole.User ? "User" : "Assistant")}: {t.Content}"));
    }

    /// <summary>
    /// Runs the streaming prompt loop and reports content via callbacks. Incoming tokens are buffered
    /// and revealed at a capped rate so the UI does not jump ahead of a comfortable reading pace.
    /// Callbacks may be invoked from a background thread; caller must marshal to UI for updates.
    /// </summary>
    /// <param name="imageBase64Contents">Optional. For vision: list of image base64 strings to send with the prompt.</param>
    /// <param name="routingUserMessage">Optional. Latest user message only; forwarded for orchestration routing so it does not grow with conversation history.</param>
    /// <param name="pipelineStatus">Optional. Receives <c>Mnemo.Core.Models.ChatPipelineStatusKeys</c> for UI pipeline labels while routing or before the first token.</param>
    /// <param name="displayOptions">Optional. Reveal pacing from <c>Chat.StreamingReveal</c>.</param>
    /// <returns>True if at least one token was received; false if empty response.</returns>
    public static async Task<(bool FoundResponse, string FinalContent)> RunStreamingAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        string fullPrompt,
        CancellationToken cancellationToken,
        Action<string> onContentUpdate,
        IReadOnlyList<string>? imageBase64Contents = null,
        string? routingUserMessage = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        ChatStreamingDisplayOptions? displayOptions = null,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var options = displayOptions ?? ChatStreamingDisplayOptions.Balanced;
        var throttledPipeline = ThrottlePipeline(pipelineStatus, minIntervalMs: 80);
        var emitContent = CreateThrottledContentEmitter(onContentUpdate, ChatStreamingDisplayOptions.DefaultUiThrottleMs);

        if (options.IsInstant)
            return await RunInstantAsync(
                orchestrator,
                systemPrompt,
                fullPrompt,
                cancellationToken,
                emitContent,
                imageBase64Contents,
                routingUserMessage,
                throttledPipeline,
                precomputedDecision,
                onToolCall,
                onAssistantReasoningUpdate).ConfigureAwait(false);

        return await RunRevealAsync(
            orchestrator,
            systemPrompt,
            fullPrompt,
            cancellationToken,
            emitContent,
            imageBase64Contents,
            routingUserMessage,
            throttledPipeline,
            precomputedDecision,
            options,
            onToolCall,
            onAssistantReasoningUpdate).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the streaming prompt loop using real multi-turn conversation history. History turns are passed
    /// as proper OpenAI-format messages instead of a flat text blob, improving multi-turn reasoning quality.
    /// </summary>
    /// <param name="systemPrompt">Base system prompt only (mode text). Skill injection is applied inside <see cref="IAIOrchestrator.PromptStreamingWithHistoryAsync"/>—do not pre-compose with <see cref="ISkillSystemPromptComposer"/>.</param>
    /// <param name="history">Prior turns (oldest first, not including the current user message).</param>
    /// <param name="userMessage">The latest user message.</param>
    /// <param name="imageBase64Contents">Optional. For vision: list of image base64 strings.</param>
    /// <param name="pipelineStatus">Optional. Receives pipeline label keys.</param>
    /// <param name="conversationRoutingKey">Optional. Thread id for last-tool routing hints (same as chat session id).</param>
    /// <param name="displayOptions">Optional. Reveal pacing.</param>
    /// <param name="onAssistantReasoningUpdate">Optional. Cumulative model reasoning for thinking models (UI thought panel).</param>
    /// <returns>True if at least one token was received; false if empty response.</returns>
    public static async Task<(bool FoundResponse, string FinalContent)> RunStreamingWithHistoryAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        CancellationToken cancellationToken,
        Action<string> onContentUpdate,
        IReadOnlyList<string>? imageBase64Contents = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null,
        ChatStreamingDisplayOptions? displayOptions = null,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var options = displayOptions ?? ChatStreamingDisplayOptions.Balanced;
        var throttledPipeline = ThrottlePipeline(pipelineStatus, minIntervalMs: 80);
        var emitContent = CreateThrottledContentEmitter(onContentUpdate, ChatStreamingDisplayOptions.DefaultUiThrottleMs);

        if (options.IsInstant)
            return await RunInstantWithHistoryAsync(
                orchestrator,
                systemPrompt,
                history,
                userMessage,
                cancellationToken,
                emitContent,
                imageBase64Contents,
                throttledPipeline,
                precomputedDecision,
                conversationRoutingKey,
                onToolCall,
                onAssistantReasoningUpdate).ConfigureAwait(false);

        return await RunRevealWithHistoryAsync(
            orchestrator,
            systemPrompt,
            history,
            userMessage,
            cancellationToken,
            emitContent,
            imageBase64Contents,
            throttledPipeline,
            precomputedDecision,
            conversationRoutingKey,
            options,
            onToolCall,
            onAssistantReasoningUpdate).ConfigureAwait(false);
    }

    private static IProgress<string>? ThrottlePipeline(IProgress<string>? inner, int minIntervalMs)
    {
        if (inner == null)
            return null;

        var last = DateTime.MinValue;
        string? lastKey = null;
        return new Progress<string>(s =>
        {
            var now = DateTime.UtcNow;
            var sameKey = string.Equals(s, lastKey, StringComparison.Ordinal);
            if (sameKey && (now - last).TotalMilliseconds < minIntervalMs)
                return;
            last = now;
            lastKey = s;
            inner.Report(s);
        });
    }

    private static Action<string, bool> CreateThrottledContentEmitter(Action<string> inner, int minIntervalMs)
    {
        var last = DateTime.MinValue;
        return (slice, force) =>
        {
            var now = DateTime.UtcNow;
            if (!force && (now - last).TotalMilliseconds < minIntervalMs)
                return;
            last = now;
            inner(slice);
        };
    }

    private static async Task<(bool FoundResponse, string FinalContent)> RunInstantAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        string fullPrompt,
        CancellationToken cancellationToken,
        Action<string, bool> emitContent,
        IReadOnlyList<string>? imageBase64Contents,
        string? routingUserMessage,
        IProgress<string>? pipelineStatus,
        RoutingAndSkillDecision? precomputedDecision,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var buffer = new StringBuilder();
        var lockObj = new object();
        var foundResponse = false;

        try
        {
            await foreach (var token in orchestrator.PromptStreamingAsync(systemPrompt, fullPrompt, cancellationToken, imageBase64Contents, routingUserMessage, pipelineStatus, precomputedDecision, null, onToolCall, onAssistantReasoningUpdate)
                .ConfigureAwait(false))
            {
                lock (lockObj)
                {
                    buffer.Append(token);
                    foundResponse = true;
                }

                string snapshot;
                lock (lockObj)
                {
                    snapshot = buffer.ToString();
                }

                emitContent(snapshot, false);
            }
        }
        catch (OperationCanceledException)
        {
            lock (lockObj)
            {
                emitContent(buffer.ToString(), true);
            }

            throw;
        }

        var finalContent = buffer.ToString();
        emitContent(finalContent, true);

        bool found;
        lock (lockObj)
        {
            found = foundResponse;
        }

        return (found, finalContent);
    }

    private static async Task<(bool FoundResponse, string FinalContent)> RunInstantWithHistoryAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        CancellationToken cancellationToken,
        Action<string, bool> emitContent,
        IReadOnlyList<string>? imageBase64Contents,
        IProgress<string>? pipelineStatus,
        RoutingAndSkillDecision? precomputedDecision,
        string? conversationRoutingKey,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var buffer = new StringBuilder();
        var lockObj = new object();
        var foundResponse = false;

        try
        {
            await foreach (var token in orchestrator.PromptStreamingWithHistoryAsync(systemPrompt, history, userMessage, cancellationToken, imageBase64Contents, pipelineStatus, precomputedDecision, conversationRoutingKey, onToolCall, onAssistantReasoningUpdate)
                .ConfigureAwait(false))
            {
                lock (lockObj)
                {
                    buffer.Append(token);
                    foundResponse = true;
                }

                string snapshot;
                lock (lockObj)
                {
                    snapshot = buffer.ToString();
                }

                emitContent(snapshot, false);
            }
        }
        catch (OperationCanceledException)
        {
            lock (lockObj)
            {
                emitContent(buffer.ToString(), true);
            }

            throw;
        }

        var finalContent = buffer.ToString();
        emitContent(finalContent, true);

        bool found;
        lock (lockObj)
        {
            found = foundResponse;
        }

        return (found, finalContent);
    }

    private static async Task<(bool FoundResponse, string FinalContent)> RunRevealWithHistoryAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        CancellationToken cancellationToken,
        Action<string, bool> emitContent,
        IReadOnlyList<string>? imageBase64Contents,
        IProgress<string>? pipelineStatus,
        RoutingAndSkillDecision? precomputedDecision,
        string? conversationRoutingKey,
        ChatStreamingDisplayOptions options,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var buffer = new StringBuilder();
        var lockObj = new object();
        var streamComplete = false;
        var revealedLength = 0;
        var foundResponse = false;

        var tickMs = Math.Max(1, options.TickMs);
        var charsPerTick = Math.Max(1, options.CharsPerTick);

        async Task ProducerAsync()
        {
            try
            {
                await foreach (var token in orchestrator.PromptStreamingWithHistoryAsync(systemPrompt, history, userMessage, cancellationToken, imageBase64Contents, pipelineStatus, precomputedDecision, conversationRoutingKey, onToolCall, onAssistantReasoningUpdate)
                    .ConfigureAwait(false))
                {
                    lock (lockObj)
                    {
                        buffer.Append(token);
                        foundResponse = true;
                    }
                }
            }
            finally
            {
                lock (lockObj)
                {
                    streamComplete = true;
                }
            }
        }

        async Task ConsumerAsync()
        {
            while (true)
            {
                await Task.Delay(tickMs, cancellationToken).ConfigureAwait(false);

                string slice;
                bool done;
                lock (lockObj)
                {
                    var len = buffer.Length;
                    if (len == 0 && !streamComplete)
                        continue;

                    if (len > 0)
                        revealedLength = Math.Min(len, revealedLength + charsPerTick);

                    slice = revealedLength == 0 ? string.Empty : buffer.ToString(0, revealedLength);
                    done = streamComplete && revealedLength >= len;
                }

                emitContent(slice, done);
                if (done)
                    break;
            }
        }

        try
        {
            await Task.WhenAll(ProducerAsync(), ConsumerAsync()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (lockObj)
            {
                emitContent(buffer.ToString(), true);
            }

            throw;
        }

        var finalContent = buffer.ToString();
        bool found;
        lock (lockObj)
        {
            found = foundResponse;
        }

        return (found, finalContent);
    }

    private static async Task<(bool FoundResponse, string FinalContent)> RunRevealAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        string fullPrompt,
        CancellationToken cancellationToken,
        Action<string, bool> emitContent,
        IReadOnlyList<string>? imageBase64Contents,
        string? routingUserMessage,
        IProgress<string>? pipelineStatus,
        RoutingAndSkillDecision? precomputedDecision,
        ChatStreamingDisplayOptions options,
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var buffer = new StringBuilder();
        var lockObj = new object();
        var streamComplete = false;
        var revealedLength = 0;
        var foundResponse = false;

        var tickMs = Math.Max(1, options.TickMs);
        var charsPerTick = Math.Max(1, options.CharsPerTick);

        async Task ProducerAsync()
        {
            try
            {
                await foreach (var token in orchestrator.PromptStreamingAsync(systemPrompt, fullPrompt, cancellationToken, imageBase64Contents, routingUserMessage, pipelineStatus, precomputedDecision, null, onToolCall, onAssistantReasoningUpdate)
                    .ConfigureAwait(false))
                {
                    lock (lockObj)
                    {
                        buffer.Append(token);
                        foundResponse = true;
                    }
                }
            }
            finally
            {
                lock (lockObj)
                {
                    streamComplete = true;
                }
            }
        }

        async Task ConsumerAsync()
        {
            while (true)
            {
                await Task.Delay(tickMs, cancellationToken).ConfigureAwait(false);

                string slice;
                bool done;
                lock (lockObj)
                {
                    var len = buffer.Length;
                    if (len == 0 && !streamComplete)
                        continue;

                    if (len > 0)
                        revealedLength = Math.Min(len, revealedLength + charsPerTick);

                    slice = revealedLength == 0 ? string.Empty : buffer.ToString(0, revealedLength);
                    done = streamComplete && revealedLength >= len;
                }

                emitContent(slice, done);
                if (done)
                    break;
            }
        }

        try
        {
            await Task.WhenAll(ProducerAsync(), ConsumerAsync()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (lockObj)
            {
                emitContent(buffer.ToString(), true);
            }

            throw;
        }

        var finalContent = buffer.ToString();
        bool found;
        lock (lockObj)
        {
            found = foundResponse;
        }

        return (found, finalContent);
    }
}
