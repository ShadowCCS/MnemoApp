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
    private readonly IOrchestrationLayer _orchestrationLayer;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillSystemPromptComposer _skillSystemPromptComposer;
    private readonly IHardwareTierEvaluator _hardwareTierEvaluator;
    private readonly HardwareDetector _hardwareDetector;
    private readonly IChatDatasetLogger _chatDatasetLogger;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly ITeacherModelClient _teacherClient;
    private readonly IRoutingToolHintStore _routingToolHintStore;
    private readonly ISkillInjectionOverrideStore _skillInjectionOverrideStore;
    private readonly IToolResultMemoryExtractor _memoryExtractor;
    private readonly IConversationMemoryStore _memoryStore;

    private readonly object _selectModelLock = new();
    private List<AIModelManifest>? _cachedModels;
    private DateTime _modelsCacheExpiry = DateTime.MinValue;

    private string? _prefetchRoutingKey;
    private RoutingResolution? _prefetchResolution;
    private RoutingAndSkillDecision? _prefetchRoutingDecision;
    private DateTime _prefetchUtc = DateTime.MinValue;

    private HardwarePerformanceTier? _cachedRoutingTier;

    public AIOrchestrator(
        IAIModelRegistry modelRegistry,
        IKnowledgeService knowledgeService,
        ISettingsService settings,
        ILoggerService logger,
        IResourceGovernor governor,
        ITextGenerationService textService,
        IOrchestrationLayer orchestrationLayer,
        ISkillRegistry skillRegistry,
        ISkillSystemPromptComposer skillSystemPromptComposer,
        IHardwareTierEvaluator hardwareTierEvaluator,
        HardwareDetector hardwareDetector,
        IChatDatasetLogger chatDatasetLogger,
        IToolDispatcher toolDispatcher,
        ITeacherModelClient teacherClient,
        IRoutingToolHintStore routingToolHintStore,
        ISkillInjectionOverrideStore skillInjectionOverrideStore,
        IToolResultMemoryExtractor memoryExtractor,
        IConversationMemoryStore memoryStore)
    {
        _modelRegistry = modelRegistry;
        _knowledgeService = knowledgeService;
        _settings = settings;
        _logger = logger;
        _governor = governor;
        _textService = textService;
        _orchestrationLayer = orchestrationLayer;
        _skillRegistry = skillRegistry;
        _skillSystemPromptComposer = skillSystemPromptComposer;
        _hardwareTierEvaluator = hardwareTierEvaluator;
        _hardwareDetector = hardwareDetector;
        _chatDatasetLogger = chatDatasetLogger;
        _toolDispatcher = toolDispatcher;
        _teacherClient = teacherClient;
        _routingToolHintStore = routingToolHintStore;
        _skillInjectionOverrideStore = skillInjectionOverrideStore;
        _memoryExtractor = memoryExtractor;
        _memoryStore = memoryStore;
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

        var memorySnapshot = string.IsNullOrWhiteSpace(conversationRoutingKey)
            ? null
            : _memoryStore.Get(conversationRoutingKey);

        if (recentHint == null && memorySnapshot?.LatestSummary == null
            && TryReusePrefetchRoutingDecisionForAnalyze(userMessage, out var fromPrefetch) && fromPrefetch != null)
            return Result<RoutingAndSkillDecision>.Success(fromPrefetch);

        var routed = await _orchestrationLayer.RouteAndClassifySkillAsync(userMessage, recentHint, ct, memorySnapshot).ConfigureAwait(false);
        if (routed.IsSuccess && routed.Value != null)
            return routed;

        _logger.Warning("AIOrchestrator", $"AnalyzeMessageAsync failed: {routed.ErrorMessage ?? "unknown error"}");
        return Result<RoutingAndSkillDecision>.Success(new RoutingAndSkillDecision
        {
            Complexity = RoutingComplexity.Simple,
            Skills = new[] { "NONE" },
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
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
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
        var finalSystemPrompt = _skillSystemPromptComposer.Compose(baseSystemPrompt, SkillIdsForCompose(targetResolution));
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

            var reasoningSbTools = new StringBuilder();
            await foreach (var token in RunToolLoopAsync(targetResolution, finalSystemPrompt, userPrompt, activeTools, conversationRoutingKey, ct, datasetRounds, pipelineStatus, onToolCall, reasoningSbTools, onAssistantReasoningUpdate).ConfigureAwait(false))
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
            var reasoningSb = new StringBuilder();
            await _governor.AcquireModelAsync(targetResolution.Model, ct).ConfigureAwait(false);
            try
            {
                await foreach (var token in StreamChatTextTokensAsync(
                                   targetResolution.Model,
                                   finalSystemPrompt,
                                   userPrompt,
                                   imageBase64Contents,
                                   ct,
                                   reasoningSb,
                                   onAssistantReasoningUpdate).ConfigureAwait(false))
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
        Action<ChatDatasetToolCall>? onToolCall = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        var targetResolution = (imageBase64Contents != null && imageBase64Contents.Count > 0)
            ? await SelectVisionModelWithProgressAsync(ct, pipelineStatus).ConfigureAwait(false)
            : await SelectModelAsync(userMessage, ct, userMessage, pipelineStatus, precomputedDecision, conversationRoutingKey).ConfigureAwait(false);
        if (targetResolution.Model == null) yield break;

        var baseSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's in-app assistant. Be concise and accurate."
            : systemPrompt;

        var finalSystemPrompt = _skillSystemPromptComposer.Compose(baseSystemPrompt, SkillIdsForCompose(targetResolution));
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

            var reasoningSbTools = new StringBuilder();
            await foreach (var token in RunToolLoopWithMessagesAsync(targetResolution, messages, activeTools, conversationRoutingKey, ct, datasetRounds, pipelineStatus, onToolCall, reasoningSbTools, onAssistantReasoningUpdate).ConfigureAwait(false))
                yield return token;

            await TryStageChatDatasetAsync(ct, targetResolution, finalSystemPrompt, userMessage,
                formattedPrompt: string.Empty, imageBase64Contents, assistantResponse: string.Empty,
                messageHistory: datasetMessageHistory, activeTools: datasetTools, toolRounds: datasetRounds).ConfigureAwait(false);
        }
        else
        {
            pipelineStatus?.Report(ChatPipelineStatusKeys.Generating);
            var sb = new StringBuilder();
            var reasoningSb = new StringBuilder();
            await _governor.AcquireModelAsync(targetResolution.Model, ct).ConfigureAwait(false);
            try
            {
                await foreach (var token in StreamChatToolChunksAsync(targetResolution.Model, messages, [], ct).ConfigureAwait(false))
                {
                    switch (token)
                    {
                        case StreamChunk.Content c:
                            sb.Append(c.Token);
                            yield return c.Token;
                            break;
                        case StreamChunk.Reasoning r:
                            reasoningSb.Append(r.Token);
                            onAssistantReasoningUpdate?.Invoke(reasoningSb.ToString());
                            break;
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
        Action<ChatDatasetToolCall>? onToolCall = null,
        StringBuilder? reasoningTurnAccumulator = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        const int MaxToolRounds = 8;

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
                        case StreamChunk.Reasoning r:
                            reasoningTurnAccumulator?.Append(r.Token);
                            if (reasoningTurnAccumulator != null)
                                onAssistantReasoningUpdate?.Invoke(reasoningTurnAccumulator.ToString());
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

            var assistantToolCalls = toolCallsThisRound.Select(AssistantToolCallEntry).ToArray();

            var assistantPreamble = NormalizeAssistantToolRoundContent(contentBuffer);
            messages.Add(new { role = "assistant", content = assistantPreamble, tool_calls = assistantToolCalls });

            var currentTurnNumber = _memoryStore.Get(conversationRoutingKey ?? string.Empty)?.TurnCount ?? 0;
            var datasetCallsThisRound = new List<ChatDatasetToolCall>(toolCallsThisRound.Count);
            foreach (var toolCall in toolCallsThisRound)
            {
                ct.ThrowIfCancellationRequested();
                pipelineStatus?.Report(ChatPipelineStatusKeys.RunningTool(toolCall.Name));
                var result = await _toolDispatcher.DispatchAsync(toolCall, new ToolDispatchScope(conversationRoutingKey), ct).ConfigureAwait(false);
                RecordRoutingToolHint(conversationRoutingKey, PrimarySkillForRoutingHint(resolution), toolCall.Name, result.Content);
                ExtractAndStoreMemoryFacts(conversationRoutingKey, toolCall.Name, result.Content, currentTurnNumber);
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
                        switch (chunk)
                        {
                            case StreamChunk.Content c:
                                yield return c.Token;
                                break;
                            case StreamChunk.Reasoning r:
                                reasoningTurnAccumulator?.Append(r.Token);
                                if (reasoningTurnAccumulator != null)
                                    onAssistantReasoningUpdate?.Invoke(reasoningTurnAccumulator.ToString());
                                break;
                        }
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
        Action<ChatDatasetToolCall>? onToolCall = null,
        StringBuilder? reasoningTurnAccumulator = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        const int MaxToolRounds = 8;

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
                        case StreamChunk.Reasoning r:
                            reasoningTurnAccumulator?.Append(r.Token);
                            if (reasoningTurnAccumulator != null)
                                onAssistantReasoningUpdate?.Invoke(reasoningTurnAccumulator.ToString());
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
            var assistantToolCalls = toolCallsThisRound.Select(AssistantToolCallEntry).ToArray();

            var assistantPreamble = NormalizeAssistantToolRoundContent(contentBuffer);
            messages.Add(new { role = "assistant", content = assistantPreamble, tool_calls = assistantToolCalls });

            // Dispatch all tool calls in this round and append their results
            var currentTurnNumber2 = _memoryStore.Get(conversationRoutingKey ?? string.Empty)?.TurnCount ?? 0;
            var datasetCallsThisRound = new List<ChatDatasetToolCall>(toolCallsThisRound.Count);
            foreach (var toolCall in toolCallsThisRound)
            {
                ct.ThrowIfCancellationRequested();
                pipelineStatus?.Report(ChatPipelineStatusKeys.RunningTool(toolCall.Name));
                var result = await _toolDispatcher.DispatchAsync(toolCall, new ToolDispatchScope(conversationRoutingKey), ct).ConfigureAwait(false);
                RecordRoutingToolHint(conversationRoutingKey, PrimarySkillForRoutingHint(resolution), toolCall.Name, result.Content);
                ExtractAndStoreMemoryFacts(conversationRoutingKey, toolCall.Name, result.Content, currentTurnNumber2);
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
                        switch (chunk)
                        {
                            case StreamChunk.Content c:
                                yield return c.Token;
                                break;
                            case StreamChunk.Reasoning r:
                                reasoningTurnAccumulator?.Append(r.Token);
                                if (reasoningTurnAccumulator != null)
                                    onAssistantReasoningUpdate?.Invoke(reasoningTurnAccumulator.ToString());
                                break;
                        }
                    }
                }
                finally
                {
                    _governor.ReleaseModel(resolution.Model!);
                }
            }
        }
    }

    /// <summary>OpenAI-style tool_calls entry; includes <c>thought_signature</c> when the provider returned one (Gemini 3 Vertex).</summary>
    private static object AssistantToolCallEntry(ToolCallRequest tc)
    {
        if (string.IsNullOrEmpty(tc.ThoughtSignature))
            return new { id = tc.Id, type = "function", function = new { name = tc.Name, arguments = tc.ArgumentsJson } };

        return new Dictionary<string, object?>
        {
            ["id"] = tc.Id,
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?> { ["name"] = tc.Name, ["arguments"] = tc.ArgumentsJson },
            ["thought_signature"] = tc.ThoughtSignature
        };
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

    private void ExtractAndStoreMemoryFacts(string? conversationRoutingKey, string toolName, string? resultContent, int turnNumber)
    {
        if (string.IsNullOrWhiteSpace(conversationRoutingKey) || string.IsNullOrWhiteSpace(resultContent))
            return;

        try
        {
            var n = 0;
            foreach (var fact in _memoryExtractor.Extract(toolName, resultContent, turnNumber))
            {
                _memoryStore.AddFact(conversationRoutingKey, fact);
                n++;
            }
            if (n > 0)
                _logger.Debug("Memory", $"Orchestrator: extracted {n} fact(s) from tool={toolName} conv={conversationRoutingKey}");
        }
        catch (Exception ex)
        {
            _logger.Warning("Memory", $"Orchestrator: fact extraction failed for tool {toolName}: {ex.Message}");
        }
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
                ResolvedSkillIds = new[] { "NONE" },
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
            var adjustedPrefetch = WithSkillInjectionOverride(cached, conversationRoutingKey);
            return await FinalizeRoutingResolutionAsync(adjustedPrefetch, ct).ConfigureAwait(false);
        }

        var resolved = await ResolveRoutingTargetModelAsync(
            routingInput,
            ct,
            pipelineStatus,
            precomputedDecision,
            routingToolHint,
            conversationRoutingKey,
            modelRoutingModeOverride: null).ConfigureAwait(false);
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
        RoutingToolHint? routingToolHint = null,
        string? conversationRoutingKey = null,
        string? modelRoutingModeOverride = null)
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
            var skillIds = precomputedDecision?.GetNormalizedSkillIds() ?? new[] { "NONE" };
            var skillId = skillIds.Count > 0 ? skillIds[0] : "NONE";
            var displayId = skillIds.Count > 1 ? string.Join(",", skillIds) : skillId;
            var res = await FinalizeRoutingResolutionAsync(
                WithSkillInjectionOverride(
                    new RoutingResolution
                    {
                        Model = models?.FirstOrDefault(m => m.Type == AIModelType.Text),
                        RoutingComplexity = RoutingComplexity.Simple,
                        SkillId = displayId,
                        ResolvedSkillIds = skillIds,
                        InjectionContext = _skillRegistry.GetMergedInjection(skillIds)
                    },
                    conversationRoutingKey),
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
                WithSkillInjectionOverride(
                    new RoutingResolution
                    {
                        Model = lowModel,
                        RoutingComplexity = RoutingComplexity.Simple,
                        SkillId = "NONE",
                        ResolvedSkillIds = new[] { "NONE" },
                        ManagerConfidence = null,
                        InjectionContext = _skillRegistry.GetInjection("NONE")
                    },
                    conversationRoutingKey),
                ct).ConfigureAwait(false);
            return (failed, null);
        }

        var hardware = _hardwareDetector.Detect();
        var tier = _cachedRoutingTier ??= _hardwareTierEvaluator.EvaluateTier(hardware);

        if (precomputedDecision == null)
            decision = ChatModelRouting.ApplyComplexityOverride(decision!, modelRoutingModeOverride);

        var normalizedIds = decision.GetNormalizedSkillIds();
        var displaySkillId = normalizedIds.Count > 1 ? string.Join(",", normalizedIds) : normalizedIds[0];
        _logger.Info("AIOrchestrator", $"Orchestration routing: complexity={decision.Complexity}, skill={displaySkillId}, hardwareTier={tier}, confidence={decision.Confidence}, reason={decision.Reason}");
        pipelineStatus?.Report(ChatPipelineStatusKeys.PreparingModel);

        var injectionContext = _skillRegistry.GetMergedInjection(normalizedIds);

        if (decision.Complexity == RoutingComplexity.Simple)
        {
            var simple = await FinalizeRoutingResolutionAsync(
                WithSkillInjectionOverride(
                    new RoutingResolution
                    {
                        Model = lowModel,
                        RoutingComplexity = RoutingComplexity.Simple,
                        SkillId = displaySkillId,
                        ResolvedSkillIds = normalizedIds,
                        ManagerConfidence = decision.Confidence,
                        InjectionContext = injectionContext
                    },
                    conversationRoutingKey),
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
            WithSkillInjectionOverride(
                new RoutingResolution
                {
                    Model = target,
                    RoutingComplexity = RoutingComplexity.Reasoning,
                    SkillId = displaySkillId,
                    ResolvedSkillIds = normalizedIds,
                    ManagerConfidence = decision.Confidence,
                    InjectionContext = injectionContext
                },
                conversationRoutingKey),
            ct).ConfigureAwait(false);
        return (reasoning, decision);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Routing prefetch that touched local llama was removed so servers start on first Send/generation.
    /// Routing cache fields remain for any future opt-in or external callers.
    /// </remarks>
    public Task PrefetchRoutingAndWarmupAsync(string routingUserMessage, string? modelRoutingMode = null, CancellationToken ct = default)
        => Task.CompletedTask;

    private RoutingResolution WithSkillInjectionOverride(RoutingResolution r, string? conversationKey)
    {
        if (string.IsNullOrWhiteSpace(conversationKey))
            return r;

        var skillOverride = _skillInjectionOverrideStore.TryGet(conversationKey);
        if (skillOverride == null)
            return r;

        var sid = skillOverride.Trim();
        if (string.IsNullOrWhiteSpace(sid) || string.Equals(sid, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            return r with
            {
                SkillId = "NONE",
                ResolvedSkillIds = new[] { "NONE" },
                InjectionContext = _skillRegistry.GetInjection("NONE")
            };
        }

        return r with
        {
            SkillId = sid,
            ResolvedSkillIds = new[] { sid },
            InjectionContext = _skillRegistry.GetInjection(sid)
        };
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
            ResolvedSkillIds = r.ResolvedSkillIds,
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
        [EnumeratorCancellation] CancellationToken ct,
        StringBuilder? reasoningTurnAccumulator = null,
        Action<string>? onAssistantReasoningUpdate = null)
    {
        if (TeacherSyntheticManifest.IsTeacher(manifest))
        {
            await foreach (var text in _teacherClient.StreamChatAsync(finalSystemPrompt, userPrompt, imageBase64Contents, ct).ConfigureAwait(false))
                yield return text;
            yield break;
        }

        var formattedPrompt = ChatPromptFormatter.Format(manifest.PromptTemplate, finalSystemPrompt, userPrompt);
        await foreach (var chunk in _textService.GenerateStreamingAsync(manifest, formattedPrompt, ct, imageBase64Contents).ConfigureAwait(false))
        {
            switch (chunk)
            {
                case StreamChunk.Content c:
                    yield return c.Token;
                    break;
                case StreamChunk.Reasoning r:
                    reasoningTurnAccumulator?.Append(r.Token);
                    if (reasoningTurnAccumulator != null)
                        onAssistantReasoningUpdate?.Invoke(reasoningTurnAccumulator.ToString());
                    break;
            }
        }
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
        /// <summary>Display id: one skill, or comma-separated when multiple.</summary>
        public string SkillId { get; init; }
        /// <summary>Ordered ids for merged injection; when null, <see cref="SkillId"/> is treated as a single skill.</summary>
        public IReadOnlyList<string>? ResolvedSkillIds { get; init; }
        public string? ManagerConfidence { get; init; }
        /// <summary>Resolved skill injection context, including enabled tool definitions for this turn.</summary>
        public SkillInjectionContext? InjectionContext { get; init; }
    }

    private static IReadOnlyList<string> SkillIdsForCompose(RoutingResolution r)
    {
        if (r.ResolvedSkillIds is { Count: > 0 })
            return r.ResolvedSkillIds;
        var s = string.IsNullOrWhiteSpace(r.SkillId) ? "NONE" : r.SkillId.Trim();
        if (s.Contains(',', StringComparison.Ordinal))
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new[] { s };
    }

    private static string PrimarySkillForRoutingHint(RoutingResolution r)
    {
        if (r.ResolvedSkillIds is { Count: > 0 })
            return r.ResolvedSkillIds[0];
        var first = r.SkillId.Split(',')[0].Trim();
        return string.IsNullOrEmpty(first) ? "NONE" : first;
    }

    public Task WarmUpLowTierModelAsync(CancellationToken ct = default)
    {
        // Local llama-server processes start on first chat Send / generation path (LlamaCppHttpTextService.EnsureRunningAsync).
        return Task.CompletedTask;
    }

    private async Task<Result<string>> ExecutePromptAsync(string systemPrompt, string userPrompt, CancellationToken ct, object? responseJsonSchema = null)
    {
        var targetResolution = await SelectModelAsync(userPrompt, ct).ConfigureAwait(false);
        if (targetResolution.Model == null) return Result<string>.Failure("No suitable text model found.");

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo's in-app assistant. Be concise and accurate."
            : systemPrompt;

        var finalSystemPrompt = _skillSystemPromptComposer.Compose(effectiveSystemPrompt, SkillIdsForCompose(targetResolution));
        var formattedPrompt = ChatPromptFormatter.Format(targetResolution.Model.PromptTemplate, finalSystemPrompt, userPrompt);
        return await PromptWithModelAsync(targetResolution.Model.Id, formattedPrompt, ct, responseJsonSchema).ConfigureAwait(false);
    }
}


