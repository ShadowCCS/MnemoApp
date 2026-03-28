using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Core.Text;

namespace Mnemo.Infrastructure.Services.AI;

public class AIOrchestrator : IAIOrchestrator
{
    private static readonly TimeSpan ModelListCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrefetchRoutingCacheTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinHeavyChatWarmSwitchInterval = TimeSpan.FromSeconds(2.5);

    /// <summary>Attempts for <see cref="IOrchestrationLayer.RouteAndClassifySkillAsync"/> before falling back to the low-tier model.</summary>
    private const int MaxRoutingAttempts = 3;

    private const double PrefetchMaxRelativeEditRatio = 0.15;
    private const int PrefetchMaxAbsoluteEditDistance = 48;
    private const int PrefetchFuzzyCompareMaxChars = 4000;

    private readonly IAIModelRegistry _modelRegistry;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly IResourceGovernor _governor;
    private readonly ITextGenerationService _textService;
    private readonly IAIServerManager _serverManager;
    private readonly IOrchestrationLayer _orchestrationLayer;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillSystemPromptComposer _skillSystemPromptComposer;
    private readonly IHardwareTierEvaluator _hardwareTierEvaluator;
    private readonly HardwareDetector _hardwareDetector;
    private readonly IChatDatasetLogger _chatDatasetLogger;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly ITeacherModelClient _teacherClient;
    private readonly IRoutingToolHintStore _routingToolHintStore;

    private readonly object _selectModelLock = new();
    private List<AIModelManifest>? _cachedModels;
    private DateTime _modelsCacheExpiry = DateTime.MinValue;

    private string? _prefetchRoutingKey;
    private RoutingResolution? _prefetchResolution;
    private RoutingAndSkillDecision? _prefetchRoutingDecision;
    private DateTime _prefetchUtc = DateTime.MinValue;

    private readonly object _prefetchWarmupThrottleLock = new();
    private DateTime _lastHeavyChatWarmUtc = DateTime.MinValue;
    private string? _lastHeavyChatWarmModelId;

    private HardwarePerformanceTier? _cachedRoutingTier;

    public AIOrchestrator(
        IAIModelRegistry modelRegistry,
        IKnowledgeService knowledgeService,
        ISettingsService settings,
        ILoggerService logger,
        IResourceGovernor governor,
        ITextGenerationService textService,
        IAIServerManager serverManager,
        IOrchestrationLayer orchestrationLayer,
        ISkillRegistry skillRegistry,
        ISkillSystemPromptComposer skillSystemPromptComposer,
        IHardwareTierEvaluator hardwareTierEvaluator,
        HardwareDetector hardwareDetector,
        IChatDatasetLogger chatDatasetLogger,
        IToolDispatcher toolDispatcher,
        ITeacherModelClient teacherClient,
        IRoutingToolHintStore routingToolHintStore)
    {
        _modelRegistry = modelRegistry;
        _knowledgeService = knowledgeService;
        _settings = settings;
        _logger = logger;
        _governor = governor;
        _textService = textService;
        _serverManager = serverManager;
        _orchestrationLayer = orchestrationLayer;
        _skillRegistry = skillRegistry;
        _skillSystemPromptComposer = skillSystemPromptComposer;
        _hardwareTierEvaluator = hardwareTierEvaluator;
        _hardwareDetector = hardwareDetector;
        _chatDatasetLogger = chatDatasetLogger;
        _toolDispatcher = toolDispatcher;
        _teacherClient = teacherClient;
        _routingToolHintStore = routingToolHintStore;
        _hardwareDetector.SnapshotInvalidated += () => _cachedRoutingTier = null;
    }

    public async Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        return await ExecutePromptAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
    }

    private async Task<List<KnowledgeChunk>?> GetRagContextAsync(string userPrompt, CancellationToken ct)
    {
        var ragEnabled = await _settings.GetAsync("AI.EnableRAG", true).ConfigureAwait(false);
        if (!ragEnabled || !ShouldUseRag(userPrompt)) return null;

        var searchQuery = userPrompt.Length > 200 ? userPrompt[..200] : userPrompt;

        var contextResult = await _knowledgeService.SearchAsync(searchQuery, 10, scopeId: null, ct).ConfigureAwait(false);
        if (contextResult.IsSuccess && contextResult.Value != null)
        {
            var relevantChunks = contextResult.Value.Where(c => c.RelevanceScore > 0.4).ToList();
            if (relevantChunks.Any())
            {
                _logger.Info("AIOrchestrator", $"Using RAG with {relevantChunks.Count} chunks (best score: {relevantChunks.Max(c => c.RelevanceScore):F2})");
                return relevantChunks;
            }
        }
        return null;
    }

    private static bool ShouldUseRag(string query)
    {
        if (query.Length < 20) return false;

        if (query.Contains("JSON") || query.Contains("STRUCTURE") || query.Contains("RESPOND ONLY WITH")) return false;

        var greetings = new[] { "hi", "hello", "hey", "how are you", "what's up", "thanks", "thank you", "bye", "goodbye" };
        var lowerQuery = query.ToLowerInvariant().Trim();
        foreach (var g in greetings)
        {
            if (lowerQuery.StartsWith(g)) return false;
        }

        return true;
    }

    public async Task<Result<string>> PromptWithContextAsync(string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default)
    {
        return await PromptWithContextAsync(string.Empty, prompt, context, ct).ConfigureAwait(false);
    }

    public async Task<Result<string>> PromptWithContextAsync(string systemPrompt, string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default, object? responseJsonSchema = null)
    {
        var condensedContext = await CondenseContextAsync(context, prompt, ct).ConfigureAwait(false);

        var finalSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's assistant. Use the provided context; if the answer is not there, say so briefly. Be accurate and concise."
            : systemPrompt;

        var fullPrompt = $"Context:\n{condensedContext}\n\nQuestion: {prompt}";

        return await ExecutePromptAsync(finalSystemPrompt, fullPrompt, ct, responseJsonSchema).ConfigureAwait(false);
    }

    private Task<string> CondenseContextAsync(IEnumerable<KnowledgeChunk> context, string query, CancellationToken ct)
    {
        var relevantChunks = context
            .OrderByDescending(c => c.RelevanceScore)
            .Take(15)
            .ToList();

        if (!relevantChunks.Any()) return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        foreach (var chunk in relevantChunks)
        {
            var safePath = chunk.Metadata.GetValueOrDefault("path", "Unknown")?.ToString()?.Replace("\\", "/");
            sb.AppendLine($"--- Source: {safePath} ---");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    public async Task<Result<string>> PromptWithModelAsync(string modelId, string prompt, CancellationToken ct = default, object? responseJsonSchema = null)
    {
        var manifest = await _modelRegistry.GetModelAsync(modelId).ConfigureAwait(false);
        if (manifest == null) return Result<string>.Failure("Model not found.");

        await _governor.AcquireModelAsync(manifest, ct).ConfigureAwait(false);
        try
        {
            _logger.Info("AIOrchestrator", $"Executing prompt with model: {manifest.DisplayName}");
            return await _textService.GenerateAsync(manifest, prompt, ct, responseJsonSchema).ConfigureAwait(false);
        }
        finally
        {
            _governor.ReleaseModel(manifest);
        }
    }

    public async Task<Result<RoutingAndSkillDecision>> AnalyzeMessageAsync(
        string userMessage,
        CancellationToken ct = default,
        IProgress<string>? pipelineStatus = null,
        string? conversationRoutingKey = null)
    {
        pipelineStatus?.Report(ChatPipelineStatusKeys.LoadingSkills);
        await _skillRegistry.LoadAsync(ct).ConfigureAwait(false);
        pipelineStatus?.Report(ChatPipelineStatusKeys.Classifying);

        var recentHint = string.IsNullOrWhiteSpace(conversationRoutingKey)
            ? null
            : _routingToolHintStore.TryGet(conversationRoutingKey);

        if (recentHint == null && TryReusePrefetchRoutingDecisionForAnalyze(userMessage, out var fromPrefetch) && fromPrefetch != null)
            return Result<RoutingAndSkillDecision>.Success(fromPrefetch);

        var routed = await _orchestrationLayer.RouteAndClassifySkillAsync(userMessage, recentHint, ct).ConfigureAwait(false);
        if (routed.IsSuccess && routed.Value != null)
            return routed;

        _logger.Warning("AIOrchestrator", $"AnalyzeMessageAsync failed: {routed.ErrorMessage ?? "unknown error"}");
        return Result<RoutingAndSkillDecision>.Success(new RoutingAndSkillDecision
        {
            Complexity = RoutingComplexity.Simple,
            Skill = "NONE",
            Confidence = null
        });
    }

    public async IAsyncEnumerable<string> PromptStreamingAsync(
        string systemPrompt,
        string userPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
        IReadOnlyList<string>? imageBase64Contents = null,
        string? routingUserMessage = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null,
        Action<ChatDatasetToolCall>? onToolCall = null)
    {
        var targetResolution = (imageBase64Contents != null && imageBase64Contents.Count > 0)
            ? await SelectVisionModelWithProgressAsync(ct, pipelineStatus).ConfigureAwait(false)
            : await SelectModelAsync(userPrompt, ct, routingUserMessage, pipelineStatus, precomputedDecision, conversationRoutingKey).ConfigureAwait(false);
        if (targetResolution.Model == null) yield break;

        var baseSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's in-app assistant. Be concise and accurate."
            : systemPrompt;

        // Gather enabled tools for this skill (only when not a vision request)
        var activeTools = imageBase64Contents == null || imageBase64Contents.Count == 0
            ? (targetResolution.InjectionContext?.Tools ?? System.Array.Empty<SkillToolDefinition>())
                .Where(t => t.Enabled)
                .ToList()
            : new List<SkillToolDefinition>();

        // Always compose the skill-augmented system prompt so the model gets skill context on both paths
        var finalSystemPrompt = _skillSystemPromptComposer.Compose(baseSystemPrompt, targetResolution.SkillId);
        finalSystemPrompt = await AppendTeacherChatStylePromptAsync(finalSystemPrompt, targetResolution.Model, ct).ConfigureAwait(false);

        if (activeTools.Count > 0)
        {
            // Tool-aware path: build message list and run agentic loop
            var datasetRounds = new List<ChatDatasetToolRound>();
            var datasetTools = ToDatasetToolDefinitions(activeTools);
            var initialMessages = new List<ChatDatasetMessage>
            {
                new() { Role = "system", Content = finalSystemPrompt },
                new() { Role = "user", Content = userPrompt }
            };

            await foreach (var token in RunToolLoopAsync(targetResolution, finalSystemPrompt, userPrompt, activeTools, conversationRoutingKey, ct, datasetRounds, pipelineStatus, onToolCall).ConfigureAwait(false))
            {
                yield return token;
            }

            await TryStageChatDatasetAsync(ct, targetResolution, finalSystemPrompt, userPrompt,
                formattedPrompt: string.Empty, imageBase64Contents, assistantResponse: string.Empty,
                messageHistory: initialMessages, activeTools: datasetTools, toolRounds: datasetRounds).ConfigureAwait(false);
        }
        else
        {
            // Standard path (no tools)
            var formattedPrompt = TeacherSyntheticManifest.IsTeacher(targetResolution.Model)
                ? string.Empty
                : ChatPromptFormatter.Format(targetResolution.Model.PromptTemplate, finalSystemPrompt, userPrompt);

            pipelineStatus?.Report(ChatPipelineStatusKeys.Generating);
            var sb = new StringBuilder();
            await _governor.AcquireModelAsync(targetResolution.Model, ct).ConfigureAwait(false);
            try
            {
                await foreach (var token in StreamChatTextTokensAsync(
                                   targetResolution.Model,
                                   finalSystemPrompt,
                                   userPrompt,
                                   imageBase64Contents,
                                   ct).ConfigureAwait(false))
                {
                    sb.Append(token);
                    yield return token;
                }
            }
            finally
            {
                _governor.ReleaseModel(targetResolution.Model);
                await TryStageChatDatasetAsync(ct, targetResolution, finalSystemPrompt, userPrompt, formattedPrompt, imageBase64Contents, sb.ToString()).ConfigureAwait(false);
            }
        }
    }

    public async IAsyncEnumerable<string> PromptStreamingWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
        IReadOnlyList<string>? imageBase64Contents = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null,
        Action<ChatDatasetToolCall>? onToolCall = null)
    {
        var targetResolution = (imageBase64Contents != null && imageBase64Contents.Count > 0)
            ? await SelectVisionModelWithProgressAsync(ct, pipelineStatus).ConfigureAwait(false)
            : await SelectModelAsync(userMessage, ct, userMessage, pipelineStatus, precomputedDecision, conversationRoutingKey).ConfigureAwait(false);
        if (targetResolution.Model == null) yield break;

        var baseSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's in-app assistant. Be concise and accurate."
            : systemPrompt;

        var finalSystemPrompt = _skillSystemPromptComposer.Compose(baseSystemPrompt, targetResolution.SkillId);
        finalSystemPrompt = await AppendTeacherChatStylePromptAsync(finalSystemPrompt, targetResolution.Model, ct).ConfigureAwait(false);

        var activeTools = imageBase64Contents == null || imageBase64Contents.Count == 0
            ? (targetResolution.InjectionContext?.Tools ?? System.Array.Empty<SkillToolDefinition>())
                .Where(t => t.Enabled)
                .ToList()
            : new List<SkillToolDefinition>();

        // Build a proper OpenAI-format message list from history
        var messages = new List<object>(history.Count + 2)
        {
            new { role = "system", content = finalSystemPrompt }
        };

        foreach (var turn in history)
        {
            messages.Add(new
            {
                role = turn.Role == ConversationRole.User ? "user" : "assistant",
                content = turn.Content
            });
        }

        // Vision: attach images to the current user message
        if (imageBase64Contents != null && imageBase64Contents.Count > 0)
        {
            var contentParts = new List<object>
            {
                new { type = "text", text = userMessage }
            };
            foreach (var b64 in imageBase64Contents)
                contentParts.Add(new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{b64}" } });

            messages.Add(new { role = "user", content = (object)contentParts });
        }
        else
        {
            messages.Add(new { role = "user", content = userMessage });
        }

        var datasetMessageHistory = BuildDatasetMessageHistory(messages);

        if (activeTools.Count > 0)
        {
            var datasetRounds = new List<ChatDatasetToolRound>();
            var datasetTools = ToDatasetToolDefinitions(activeTools);

            await foreach (var token in RunToolLoopWithMessagesAsync(targetResolution, messages, activeTools, conversationRoutingKey, ct, datasetRounds, pipelineStatus, onToolCall).ConfigureAwait(false))
                yield return token;

            await TryStageChatDatasetAsync(ct, targetResolution, finalSystemPrompt, userMessage,
                formattedPrompt: string.Empty, imageBase64Contents, assistantResponse: string.Empty,
                messageHistory: datasetMessageHistory, activeTools: datasetTools, toolRounds: datasetRounds).ConfigureAwait(false);
        }
        else
        {
            pipelineStatus?.Report(ChatPipelineStatusKeys.Generating);
            var sb = new StringBuilder();
            await _governor.AcquireModelAsync(targetResolution.Model, ct).ConfigureAwait(false);
            try
            {
                await foreach (var token in StreamChatToolChunksAsync(targetResolution.Model, messages, [], ct).ConfigureAwait(false))
                {
                    if (token is StreamChunk.Content c)
                    {
                        sb.Append(c.Token);
                        yield return c.Token;
                    }
                }
            }
            finally
            {
                _governor.ReleaseModel(targetResolution.Model);
                await TryStageChatDatasetAsync(ct, targetResolution, finalSystemPrompt, userMessage,
                    formattedPrompt: string.Empty, imageBase64Contents, sb.ToString(),
                    messageHistory: datasetMessageHistory).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Agentic tool-call loop operating on a pre-built message list (used by the history-aware path).
    /// Streams assistant text as it arrives; when the model issues tool calls, that text is kept in the
    /// message history alongside <c>tool_calls</c> so the next generation pass can continue coherently.
    /// </summary>
    private async IAsyncEnumerable<string> RunToolLoopWithMessagesAsync(
        RoutingResolution resolution,
        List<object> messages,
        IReadOnlyList<SkillToolDefinition> tools,
        string? conversationRoutingKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        List<ChatDatasetToolRound>? datasetRounds = null,
        IProgress<string>? pipelineStatus = null,
        Action<ChatDatasetToolCall>? onToolCall = null)
    {
        const int MaxToolRounds = 5;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            pipelineStatus?.Report(round == 0 ? ChatPipelineStatusKeys.Generating : ChatPipelineStatusKeys.ContinuingAfterTool);

            var contentBuffer = new StringBuilder();
            var toolCallsThisRound = new List<ToolCallRequest>();

            await _governor.AcquireModelAsync(resolution.Model!, ct).ConfigureAwait(false);
            try
            {
                await foreach (var chunk in StreamChatToolChunksAsync(resolution.Model!, messages, tools, ct).ConfigureAwait(false))
                {
                    switch (chunk)
                    {
                        case StreamChunk.Content c:
                            contentBuffer.Append(c.Token);
                            yield return c.Token;
                            break;
                        case StreamChunk.ToolCall tc:
                            toolCallsThisRound.Add(tc.Request);
                            break;
                    }
                }
            }
            finally
            {
                _governor.ReleaseModel(resolution.Model!);
            }

            if (toolCallsThisRound.Count == 0)
            {
                if (contentBuffer.Length == 0)
                    _logger.Warning("AIOrchestrator", $"Tool loop round {round + 1}: model returned empty final response.");
                yield break;
            }

            _logger.Info("AIOrchestrator", $"Tool-call round {round + 1}: model requested {toolCallsThisRound.Count} tool(s).");

            var assistantToolCalls = toolCallsThisRound.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.ArgumentsJson }
            }).ToArray();

            var assistantPreamble = NormalizeAssistantToolRoundContent(contentBuffer);
            messages.Add(new { role = "assistant", content = assistantPreamble, tool_calls = assistantToolCalls });

            var datasetCallsThisRound = new List<ChatDatasetToolCall>(toolCallsThisRound.Count);
            foreach (var toolCall in toolCallsThisRound)
            {
                ct.ThrowIfCancellationRequested();
                pipelineStatus?.Report(ChatPipelineStatusKeys.RunningTool(toolCall.Name));
                var result = await _toolDispatcher.DispatchAsync(toolCall, ct).ConfigureAwait(false);
                RecordRoutingToolHint(conversationRoutingKey, resolution.SkillId, toolCall.Name, result.Content);
                messages.Add(new { role = "tool", tool_call_id = result.ToolCallId, name = result.Name, content = result.Content });
                
                var dsCall = new ChatDatasetToolCall
                {
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Name,
                    ArgumentsJson = toolCall.ArgumentsJson,
                    ResultContent = result.Content
                };
                datasetCallsThisRound.Add(dsCall);
                onToolCall?.Invoke(dsCall);
            }

            datasetRounds?.Add(new ChatDatasetToolRound { Round = round + 1, ToolCalls = datasetCallsThisRound });

            if (round == MaxToolRounds - 1)
            {
                _logger.Warning("AIOrchestrator", $"Tool-call loop reached max rounds ({MaxToolRounds}); forcing final text response.");
                pipelineStatus?.Report(ChatPipelineStatusKeys.Generating);
                await _governor.AcquireModelAsync(resolution.Model!, ct).ConfigureAwait(false);
                try
                {
                    await foreach (var chunk in StreamChatToolChunksAsync(resolution.Model!, messages, [], ct).ConfigureAwait(false))
                    {
                        if (chunk is StreamChunk.Content c)
                            yield return c.Token;
                    }
                }
                finally
                {
                    _governor.ReleaseModel(resolution.Model!);
                }
            }
        }
    }

    /// <summary>
    /// Agentic tool-call loop. Builds a message list, streams generation, dispatches any tool calls,
    /// appends results, and re-prompts until the model emits a final text response or the round cap is reached.
    /// Partial assistant text before each tool round is streamed to the caller and preserved in history.
    /// </summary>
    private async IAsyncEnumerable<string> RunToolLoopAsync(
        RoutingResolution resolution,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<SkillToolDefinition> tools,
        string? conversationRoutingKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        List<ChatDatasetToolRound>? datasetRounds = null,
        IProgress<string>? pipelineStatus = null,
        Action<ChatDatasetToolCall>? onToolCall = null)
    {
        const int MaxToolRounds = 5;

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        for (var round = 0; round < MaxToolRounds; round++)
        {
            pipelineStatus?.Report(round == 0 ? ChatPipelineStatusKeys.Generating : ChatPipelineStatusKeys.ContinuingAfterTool);

            var contentBuffer = new StringBuilder();
            var toolCallsThisRound = new List<ToolCallRequest>();

            await _governor.AcquireModelAsync(resolution.Model!, ct).ConfigureAwait(false);
            try
            {
                await foreach (var chunk in StreamChatToolChunksAsync(resolution.Model!, messages, tools, ct).ConfigureAwait(false))
                {
                    switch (chunk)
                    {
                        case StreamChunk.Content c:
                            contentBuffer.Append(c.Token);
                            yield return c.Token;
                            break;
                        case StreamChunk.ToolCall tc:
                            toolCallsThisRound.Add(tc.Request);
                            break;
                    }
                }
            }
            finally
            {
                _governor.ReleaseModel(resolution.Model!);
            }

            // No tool calls → model produced its final answer (already streamed token-by-token)
            if (toolCallsThisRound.Count == 0)
            {
                if (contentBuffer.Length == 0)
                    _logger.Warning("AIOrchestrator", $"Tool loop round {round + 1}: model returned empty final response.");
                yield break;
            }

            _logger.Info("AIOrchestrator", $"Tool-call round {round + 1}: model requested {toolCallsThisRound.Count} tool(s).");

            // Build the assistant message that carries the tool-call requests
            var assistantToolCalls = toolCallsThisRound.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.ArgumentsJson }
            }).ToArray();

            var assistantPreamble = NormalizeAssistantToolRoundContent(contentBuffer);
            messages.Add(new { role = "assistant", content = assistantPreamble, tool_calls = assistantToolCalls });

            // Dispatch all tool calls in this round and append their results
            var datasetCallsThisRound = new List<ChatDatasetToolCall>(toolCallsThisRound.Count);
            foreach (var toolCall in toolCallsThisRound)
            {
                ct.ThrowIfCancellationRequested();
                pipelineStatus?.Report(ChatPipelineStatusKeys.RunningTool(toolCall.Name));
                var result = await _toolDispatcher.DispatchAsync(toolCall, ct).ConfigureAwait(false);
                RecordRoutingToolHint(conversationRoutingKey, resolution.SkillId, toolCall.Name, result.Content);
                messages.Add(new { role = "tool", tool_call_id = result.ToolCallId, name = result.Name, content = result.Content });
                
                var dsCall = new ChatDatasetToolCall
                {
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Name,
                    ArgumentsJson = toolCall.ArgumentsJson,
                    ResultContent = result.Content
                };
                datasetCallsThisRound.Add(dsCall);
                onToolCall?.Invoke(dsCall);
            }

            datasetRounds?.Add(new ChatDatasetToolRound { Round = round + 1, ToolCalls = datasetCallsThisRound });

            // If the round cap was hit while there are still tool calls pending, do one final pass
            // without tools so the model must produce a text summary instead of looping further
            if (round == MaxToolRounds - 1)
            {
                _logger.Warning("AIOrchestrator", $"Tool-call loop reached max rounds ({MaxToolRounds}); forcing final text response.");
                pipelineStatus?.Report(ChatPipelineStatusKeys.Generating);
                await _governor.AcquireModelAsync(resolution.Model!, ct).ConfigureAwait(false);
                try
                {
                    await foreach (var chunk in StreamChatToolChunksAsync(resolution.Model!, messages, [], ct).ConfigureAwait(false))
                    {
                        if (chunk is StreamChunk.Content c)
                            yield return c.Token;
                    }
                }
                finally
                {
                    _governor.ReleaseModel(resolution.Model!);
                }
            }
        }
    }

    /// <summary>
    /// OpenAI-style assistant <c>content</c> for a turn that also includes <c>tool_calls</c>: omit when empty so payloads stay compact.
    /// </summary>
    private static string? NormalizeAssistantToolRoundContent(StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return null;

        var s = buffer.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private void RecordRoutingToolHint(string? conversationRoutingKey, string skillId, string toolName, string? resultContent)
    {
        if (string.IsNullOrWhiteSpace(conversationRoutingKey))
            return;

        _routingToolHintStore.Record(conversationRoutingKey, skillId, toolName, TruncateRoutingDetail(resultContent));
    }

    private static string? TruncateRoutingDetail(string? s, int maxLen = 400)
    {
        if (string.IsNullOrEmpty(s))
            return null;

        s = s.Trim();
        if (s.Length <= maxLen)
            return s;

        return s[..maxLen] + "…";
    }

    private async Task TryStageChatDatasetAsync(
        CancellationToken ct,
        RoutingResolution resolution,
        string finalSystemPrompt,
        string userPromptFull,
        string formattedPrompt,
        IReadOnlyList<string>? imageBase64Contents,
        string assistantResponse,
        IReadOnlyList<ChatDatasetMessage>? messageHistory = null,
        IReadOnlyList<ChatDatasetToolDefinition>? activeTools = null,
        IReadOnlyList<ChatDatasetToolRound>? toolRounds = null)
    {
        try
        {
            var turnId = ChatDatasetLoggingScope.CurrentTurnId;
            if (string.IsNullOrEmpty(turnId)) return;
            if (!await _settings.GetAsync(ChatDatasetSettings.LoggingEnabledKey, false).ConfigureAwait(false)) return;

            var m = resolution.Model!;
            var section = new ChatDatasetChatSection
            {
                ModelId = m.Id,
                ModelDisplayName = m.DisplayName,
                PromptTemplate = m.PromptTemplate,
                SystemPrompt = finalSystemPrompt,
                UserPromptFull = userPromptFull,
                FormattedPrompt = formattedPrompt,
                ImageAttachmentCount = imageBase64Contents?.Count ?? 0,
                RoutingComplexity = resolution.RoutingComplexity.ToString(),
                SkillId = resolution.SkillId,
                ManagerConfidence = resolution.ManagerConfidence,
                AssistantResponse = assistantResponse,
                MessageHistory = messageHistory,
                ActiveTools = activeTools,
                ToolRounds = toolRounds
            };
            await _chatDatasetLogger.StageChatAsync(turnId, section, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("AIOrchestrator", $"Chat dataset staging failed: {ex.Message}");
        }
    }

    private async Task<RoutingResolution> SelectVisionModelWithProgressAsync(CancellationToken ct, IProgress<string>? pipelineStatus)
    {
        pipelineStatus?.Report(ChatPipelineStatusKeys.PreparingModel);
        var model = await SelectVisionModelAsync(ct).ConfigureAwait(false);
        return await FinalizeRoutingResolutionAsync(
            new RoutingResolution
            {
                Model = model,
                RoutingComplexity = RoutingComplexity.Simple,
                SkillId = "NONE",
                ManagerConfidence = null
            },
            ct).ConfigureAwait(false);
    }

    private async Task<AIModelManifest?> SelectVisionModelAsync(CancellationToken ct)
    {
        var modelsEnumerable = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
        var low = modelsEnumerable.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Low);
        return low ?? modelsEnumerable.FirstOrDefault(m => m.Type == AIModelType.Text);
    }

    /// <param name="routingUserMessage">When non-null, used for routing and simple-prompt detection only; <paramref name="userPrompt"/> is still the full prompt for generation elsewhere.</param>
    /// <param name="conversationRoutingKey">When set, last tool hint for this thread may influence routing; prefetch cache is skipped if a hint exists (or when <paramref name="precomputedDecision"/> is set).</param>
    private async Task<RoutingResolution> SelectModelAsync(
        string userPrompt,
        CancellationToken ct,
        string? routingUserMessage = null,
        IProgress<string>? pipelineStatus = null,
        RoutingAndSkillDecision? precomputedDecision = null,
        string? conversationRoutingKey = null)
    {
        var routingInput = string.IsNullOrEmpty(routingUserMessage) ? userPrompt : routingUserMessage;

        RoutingToolHint? routingToolHint = null;
        if (!string.IsNullOrWhiteSpace(conversationRoutingKey))
            routingToolHint = _routingToolHintStore.TryGet(conversationRoutingKey);

        if (precomputedDecision == null && routingToolHint == null && TryConsumePrefetchRoutingCache(routingInput, out var cached))
        {
            _logger.Debug("AIOrchestrator", "Using cached prefetch routing result.");
            pipelineStatus?.Report(ChatPipelineStatusKeys.PreparingModel);
            return await FinalizeRoutingResolutionAsync(cached, ct).ConfigureAwait(false);
        }

        var resolved = await ResolveRoutingTargetModelAsync(routingInput, ct, pipelineStatus, precomputedDecision, routingToolHint).ConfigureAwait(false);
        return resolved.Resolution;
    }

    /// <summary>
    /// When <see cref="PrefetchRoutingAndWarmupAsync"/> already ran the teacher/manager for this draft,
    /// <see cref="AnalyzeMessageAsync"/> can return the same decision without a second remote call.
    /// Skipped when a per-thread tool hint applies (prefetch did not use it).
    /// </summary>
    private bool TryReusePrefetchRoutingDecisionForAnalyze(string userMessage, out RoutingAndSkillDecision? decision)
    {
        decision = null;
        lock (_selectModelLock)
        {
            if (_prefetchRoutingKey == null || _prefetchResolution == null || _prefetchRoutingDecision == null)
                return false;
            if (DateTime.UtcNow - _prefetchUtc > PrefetchRoutingCacheTtl)
                return false;
            if (!IsPrefetchRoutingKeyCompatible(userMessage, _prefetchRoutingKey))
                return false;
            _logger.Debug("AIOrchestrator", "AnalyzeMessageAsync: reusing prefetch routing decision (no duplicate routing call).");
            decision = _prefetchRoutingDecision;
            return true;
        }
    }

    private bool TryConsumePrefetchRoutingCache(string routingInput, out RoutingResolution model)
    {
        lock (_selectModelLock)
        {
            model = default;
            if (_prefetchResolution == null || _prefetchRoutingKey == null)
                return false;
            if (DateTime.UtcNow - _prefetchUtc > PrefetchRoutingCacheTtl)
                return false;
            if (!IsPrefetchRoutingKeyCompatible(routingInput, _prefetchRoutingKey))
                return false;
            model = _prefetchResolution.Value;
            return true;
        }
    }

    private static bool IsPrefetchRoutingKeyCompatible(string send, string? cached)
    {
        if (cached == null) return false;
        if (string.Equals(send, cached, StringComparison.Ordinal))
            return true;

        var a = send.TrimEnd();
        var b = cached.TrimEnd();
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        if (a.Length > PrefetchFuzzyCompareMaxChars || b.Length > PrefetchFuzzyCompareMaxChars)
            return false;

        return TextEditDistance.IsWithinRelativeEditDistance(a, b, PrefetchMaxRelativeEditRatio, PrefetchMaxAbsoluteEditDistance);
    }

    private void SetPrefetchRoutingCache(string routingInput, RoutingResolution resolution, RoutingAndSkillDecision? routingDecision)
    {
        lock (_selectModelLock)
        {
            _prefetchRoutingKey = routingInput;
            _prefetchResolution = resolution;
            _prefetchRoutingDecision = routingDecision;
            _prefetchUtc = DateTime.UtcNow;
        }
    }

    private async Task<(RoutingResolution Resolution, RoutingAndSkillDecision? Decision)> ResolveRoutingTargetModelAsync(
        string routingInput,
        CancellationToken ct,
        IProgress<string>? pipelineStatus,
        RoutingAndSkillDecision? precomputedDecision = null,
        RoutingToolHint? routingToolHint = null)
    {
        var now = DateTime.UtcNow;

        if (_cachedModels == null || now >= _modelsCacheExpiry)
        {
            var modelsEnumerable = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
            var list = modelsEnumerable.ToList();
            lock (_selectModelLock)
            {
                _cachedModels = list;
                _modelsCacheExpiry = DateTime.UtcNow + ModelListCacheTtl;
            }
        }

        List<AIModelManifest>? models;
        lock (_selectModelLock)
        {
            models = _cachedModels;
        }

        var lowModel = models?.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Low);
        var midModel = models?.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Mid);
        var highModel = models?.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.High);

        if (lowModel == null)
        {
            var skillId = precomputedDecision?.Skill ?? "NONE";
            var res = await FinalizeRoutingResolutionAsync(
                new RoutingResolution
                {
                    Model = models?.FirstOrDefault(m => m.Type == AIModelType.Text),
                    RoutingComplexity = RoutingComplexity.Simple,
                    SkillId = skillId,
                    InjectionContext = _skillRegistry.GetInjection(skillId)
                },
                ct).ConfigureAwait(false);
            return (res, precomputedDecision);
        }

        RoutingAndSkillDecision? decision = precomputedDecision;
        string? lastRoutingError = null;
        if (decision == null)
        {
            pipelineStatus?.Report(ChatPipelineStatusKeys.LoadingSkills);
            await _skillRegistry.LoadAsync(ct).ConfigureAwait(false);
            pipelineStatus?.Report(ChatPipelineStatusKeys.Classifying);
            for (var attempt = 1; attempt <= MaxRoutingAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var routeResult = await _orchestrationLayer.RouteAndClassifySkillAsync(routingInput, routingToolHint, ct).ConfigureAwait(false);
                if (routeResult.IsSuccess && routeResult.Value != null)
                {
                    decision = routeResult.Value;
                    break;
                }

                lastRoutingError = routeResult.ErrorMessage;
                if (attempt < MaxRoutingAttempts)
                {
                    _logger.Warning(
                        "AIOrchestrator",
                        $"Routing attempt {attempt}/{MaxRoutingAttempts} failed ({routeResult.ErrorMessage}); retrying.");
                }
            }
        }

        if (decision == null)
        {
            _logger.Error(
                "AIOrchestrator",
                $"Routing failed after {MaxRoutingAttempts} attempts: {lastRoutingError ?? "unknown error"}; using low-tier model.");
            pipelineStatus?.Report(ChatPipelineStatusKeys.PreparingModel);
            var failed = await FinalizeRoutingResolutionAsync(
                new RoutingResolution
                {
                    Model = lowModel,
                    RoutingComplexity = RoutingComplexity.Simple,
                    SkillId = "NONE",
                    ManagerConfidence = null,
                    InjectionContext = _skillRegistry.GetInjection("NONE")
                },
                ct).ConfigureAwait(false);
            return (failed, null);
        }

        var hardware = _hardwareDetector.Detect();
        var tier = _cachedRoutingTier ??= _hardwareTierEvaluator.EvaluateTier(hardware);

        _logger.Info("AIOrchestrator", $"Orchestration routing: complexity={decision.Complexity}, skill={decision.Skill}, hardwareTier={tier}, confidence={decision.Confidence}, reason={decision.Reason}");
        pipelineStatus?.Report(ChatPipelineStatusKeys.PreparingModel);

        var injectionContext = _skillRegistry.GetInjection(decision.Skill);

        if (decision.Complexity == RoutingComplexity.Simple)
        {
            var simple = await FinalizeRoutingResolutionAsync(
                new RoutingResolution
                {
                    Model = lowModel,
                    RoutingComplexity = RoutingComplexity.Simple,
                    SkillId = decision.Skill,
                    ManagerConfidence = decision.Confidence,
                    InjectionContext = injectionContext
                },
                ct).ConfigureAwait(false);
            return (simple, decision);
        }

        var target = tier switch
        {
            HardwarePerformanceTier.Low => lowModel,
            HardwarePerformanceTier.Mid => midModel ?? lowModel,
            HardwarePerformanceTier.High => highModel ?? midModel ?? lowModel,
            _ => lowModel
        };

        var reasoning = await FinalizeRoutingResolutionAsync(
            new RoutingResolution
            {
                Model = target,
                RoutingComplexity = RoutingComplexity.Reasoning,
                SkillId = decision.Skill,
                ManagerConfidence = decision.Confidence,
                InjectionContext = injectionContext
            },
            ct).ConfigureAwait(false);
        return (reasoning, decision);
    }

    public async Task PrefetchRoutingAndWarmupAsync(string routingUserMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routingUserMessage))
            return;

        var (resolution, routingDecision) = await ResolveRoutingTargetModelAsync(routingUserMessage, ct, pipelineStatus: null).ConfigureAwait(false);
        if (resolution.Model == null)
            return;

        if (TeacherSyntheticManifest.IsTeacher(resolution.Model))
        {
            SetPrefetchRoutingCache(routingUserMessage, resolution, routingDecision);
            return;
        }

        if (resolution.RoutingComplexity == RoutingComplexity.Reasoning
            && string.Equals(resolution.ManagerConfidence, "low", StringComparison.OrdinalIgnoreCase)
            && IsHeavyChatModel(resolution.Model))
        {
            _logger.Debug("AIOrchestrator", "Skipping prefetch routing cache and warm-up (manager confidence low for reasoning; would load mid/high tier).");
            return;
        }

        SetPrefetchRoutingCache(routingUserMessage, resolution, routingDecision);

        if (ShouldThrottleHeavyPrefetchWarm(resolution.Model))
        {
            _logger.Debug("AIOrchestrator", "Deferring heavy chat model prefetch warm-up (rapid switch throttle).");
            return;
        }

        try
        {
            await _serverManager.EnsureRunningAsync(resolution.Model, ct).ConfigureAwait(false);
            RecordHeavyPrefetchWarm(resolution.Model);
        }
        catch (Exception ex)
        {
            _logger.Warning("AIOrchestrator", $"Prefetch warm-up failed: {ex.Message}");
        }
    }

    private static bool IsHeavyChatModel(AIModelManifest model)
    {
        var role = model.Role;
        return role == AIModelRoles.Mid || role == AIModelRoles.High;
    }

    private bool ShouldThrottleHeavyPrefetchWarm(AIModelManifest model)
    {
        if (!IsHeavyChatModel(model))
            return false;

        lock (_prefetchWarmupThrottleLock)
        {
            var now = DateTime.UtcNow;
            if (_lastHeavyChatWarmUtc != DateTime.MinValue
                && now - _lastHeavyChatWarmUtc < MinHeavyChatWarmSwitchInterval
                && _lastHeavyChatWarmModelId != null
                && _lastHeavyChatWarmModelId != model.Id)
            {
                return true;
            }
        }

        return false;
    }

    private void RecordHeavyPrefetchWarm(AIModelManifest model)
    {
        if (!IsHeavyChatModel(model))
            return;

        lock (_prefetchWarmupThrottleLock)
        {
            _lastHeavyChatWarmUtc = DateTime.UtcNow;
            _lastHeavyChatWarmModelId = model.Id;
        }
    }

    private async Task<RoutingResolution> FinalizeRoutingResolutionAsync(RoutingResolution r, CancellationToken ct)
    {
        if (!await _settings.GetAsync(TeacherModelSettings.UseTeacherMainChatKey, false).ConfigureAwait(false))
            return r;
        if (!await _teacherClient.IsConfiguredAsync(ct).ConfigureAwait(false))
            return r;
        return new RoutingResolution
        {
            Model = TeacherSyntheticManifest.CreateChatModel(),
            RoutingComplexity = r.RoutingComplexity,
            SkillId = r.SkillId,
            ManagerConfidence = r.ManagerConfidence,
            InjectionContext = r.InjectionContext
        };
    }

    private static IReadOnlyList<ChatDatasetToolDefinition> ToDatasetToolDefinitions(IReadOnlyList<SkillToolDefinition> tools)
    {
        var result = new List<ChatDatasetToolDefinition>(tools.Count);
        foreach (var t in tools)
        {
            result.Add(new ChatDatasetToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                ParametersJson = t.Parameters.ValueKind != System.Text.Json.JsonValueKind.Undefined
                    ? t.Parameters.GetRawText()
                    : null
            });
        }
        return result;
    }

    /// <summary>
    /// Converts the OpenAI-format message list to dataset records, extracting text content only
    /// (skips vision parts and assistant tool_calls entries which are captured in ToolRounds).
    /// </summary>
    private static IReadOnlyList<ChatDatasetMessage> BuildDatasetMessageHistory(IReadOnlyList<object> messages)
    {
        var result = new List<ChatDatasetMessage>(messages.Count);
        foreach (var msg in messages)
        {
            // Use JSON round-trip to safely read the anonymous-type fields
            var json = System.Text.Json.JsonSerializer.Serialize(msg);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("role", out var roleProp)) continue;
            var role = roleProp.GetString() ?? "";

            // Skip assistant rows that are tool-call-only (no user-visible text); tool rounds are in ToolRounds
            if (role == "assistant" && root.TryGetProperty("tool_calls", out _))
            {
                var hasPreamble = root.TryGetProperty("content", out var ac)
                    && ac.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(ac.GetString());
                if (!hasPreamble)
                    continue;
            }

            string? content = null;
            if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == System.Text.Json.JsonValueKind.String)
                content = contentProp.GetString();

            string? toolCallId = null;
            string? toolName = null;
            if (role == "tool")
            {
                if (root.TryGetProperty("tool_call_id", out var tcid)) toolCallId = tcid.GetString();
                if (root.TryGetProperty("name", out var tn)) toolName = tn.GetString();
            }

            result.Add(new ChatDatasetMessage
            {
                Role = role,
                Content = content,
                ToolCallId = toolCallId,
                ToolName = toolName
            });
        }
        return result;
    }

    /// <summary>
    /// Optional extra system text when the main model is the Vertex teacher (developer setting), for tone and answer shape in logged data.
    /// </summary>
    private async Task<string> AppendTeacherChatStylePromptAsync(string finalSystemPrompt, AIModelManifest? model, CancellationToken ct)
    {
        if (model == null || !TeacherSyntheticManifest.IsTeacher(model))
            return finalSystemPrompt;

        var extra = await _settings.GetAsync(TeacherModelSettings.ChatStylePromptKey, "").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(extra))
            return finalSystemPrompt;

        return $"{finalSystemPrompt.TrimEnd()}\n\n{extra.Trim()}";
    }

    private async IAsyncEnumerable<string> StreamChatTextTokensAsync(
        AIModelManifest manifest,
        string finalSystemPrompt,
        string userPrompt,
        IReadOnlyList<string>? imageBase64Contents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (TeacherSyntheticManifest.IsTeacher(manifest))
        {
            await foreach (var chunk in _teacherClient.StreamChatAsync(finalSystemPrompt, userPrompt, imageBase64Contents, ct).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }

        var formattedPrompt = ChatPromptFormatter.Format(manifest.PromptTemplate, finalSystemPrompt, userPrompt);
        await foreach (var chunk in _textService.GenerateStreamingAsync(manifest, formattedPrompt, ct, imageBase64Contents).ConfigureAwait(false))
            yield return chunk;
    }

    private async IAsyncEnumerable<StreamChunk> StreamChatToolChunksAsync(
        AIModelManifest manifest,
        IReadOnlyList<object> messages,
        IReadOnlyList<SkillToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (TeacherSyntheticManifest.IsTeacher(manifest))
        {
            await foreach (var chunk in _teacherClient.StreamChatWithToolsAsync(messages, tools, ct).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }

        await foreach (var chunk in _textService.GenerateStreamingWithToolsAsync(manifest, messages, tools, ct).ConfigureAwait(false))
            yield return chunk;
    }

    private readonly struct RoutingResolution
    {
        public AIModelManifest? Model { get; init; }
        public RoutingComplexity RoutingComplexity { get; init; }
        public string SkillId { get; init; }
        public string? ManagerConfidence { get; init; }
        /// <summary>Resolved skill injection context, including enabled tool definitions for this turn.</summary>
        public SkillInjectionContext? InjectionContext { get; init; }
    }

    public async Task WarmUpLowTierModelAsync(CancellationToken ct = default)
    {
        var teacherMain = await _settings.GetAsync(TeacherModelSettings.UseTeacherMainChatKey, false).ConfigureAwait(false)
            && await _teacherClient.IsConfiguredAsync(ct).ConfigureAwait(false);
        var teacherRouting = await _settings.GetAsync(TeacherModelSettings.UseTeacherRoutingKey, false).ConfigureAwait(false)
            && await _teacherClient.IsConfiguredAsync(ct).ConfigureAwait(false);

        var models = (await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false)).ToList();
        var manager = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Manager);
        var lowModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Low);

        if (manager != null && !teacherRouting)
        {
            try
            {
                await _serverManager.EnsureRunningAsync(manager, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning("AIOrchestrator", $"Orchestration model warm-up failed: {ex.Message}");
            }
        }

        if (teacherMain)
            return;

        if (lowModel == null)
            return;

        try
        {
            await _serverManager.EnsureRunningAsync(lowModel, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("AIOrchestrator", $"Low-tier model warm-up failed: {ex.Message}");
        }
    }

    private async Task<Result<string>> ExecutePromptAsync(string systemPrompt, string userPrompt, CancellationToken ct, object? responseJsonSchema = null)
    {
        var targetResolution = await SelectModelAsync(userPrompt, ct).ConfigureAwait(false);
        if (targetResolution.Model == null) return Result<string>.Failure("No suitable text model found.");

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's in-app assistant. Be concise and accurate."
            : systemPrompt;

        var finalSystemPrompt = _skillSystemPromptComposer.Compose(effectiveSystemPrompt, targetResolution.SkillId);
        var formattedPrompt = ChatPromptFormatter.Format(targetResolution.Model.PromptTemplate, finalSystemPrompt, userPrompt);
        return await PromptWithModelAsync(targetResolution.Model.Id, formattedPrompt, ct, responseJsonSchema).ConfigureAwait(false);
    }
}


