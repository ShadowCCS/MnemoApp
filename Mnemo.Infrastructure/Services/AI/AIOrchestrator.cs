using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Attempts for <see cref="IOrchestrationLayer.RouteUserMessageAsync"/> before falling back to the low-tier model.</summary>
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
    private readonly IHardwareTierEvaluator _hardwareTierEvaluator;
    private readonly HardwareDetector _hardwareDetector;

    private readonly object _selectModelLock = new();
    private List<AIModelManifest>? _cachedModels;
    private DateTime _modelsCacheExpiry = DateTime.MinValue;

    private string? _prefetchRoutingKey;
    private AIModelManifest? _prefetchModel;
    private DateTime _prefetchUtc = DateTime.MinValue;

    private readonly object _prefetchWarmupThrottleLock = new();
    private DateTime _lastHeavyChatWarmUtc = DateTime.MinValue;
    private string? _lastHeavyChatWarmModelId;

    public AIOrchestrator(
        IAIModelRegistry modelRegistry,
        IKnowledgeService knowledgeService,
        ISettingsService settings,
        ILoggerService logger,
        IResourceGovernor governor,
        ITextGenerationService textService,
        IAIServerManager serverManager,
        IOrchestrationLayer orchestrationLayer,
        IHardwareTierEvaluator hardwareTierEvaluator,
        HardwareDetector hardwareDetector)
    {
        _modelRegistry = modelRegistry;
        _knowledgeService = knowledgeService;
        _settings = settings;
        _logger = logger;
        _governor = governor;
        _textService = textService;
        _serverManager = serverManager;
        _orchestrationLayer = orchestrationLayer;
        _hardwareTierEvaluator = hardwareTierEvaluator;
        _hardwareDetector = hardwareDetector;
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
            ? "You are Mnemo, an expert assistant. Use the provided context to answer the user's question accurately. If the answer is not in the context, politely state that you don't have that information. Keep answers professional and grounded in the provided facts."
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

    public async IAsyncEnumerable<string> PromptStreamingAsync(string systemPrompt, string userPrompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, IReadOnlyList<string>? imageBase64Contents = null, string? routingUserMessage = null, IProgress<string>? pipelineStatus = null)
    {
        var targetModel = (imageBase64Contents != null && imageBase64Contents.Count > 0)
            ? await SelectVisionModelWithProgressAsync(ct, pipelineStatus).ConfigureAwait(false)
            : await SelectModelAsync(userPrompt, ct, routingUserMessage, pipelineStatus).ConfigureAwait(false);
        if (targetModel == null) yield break;

        var finalSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo, a helpful and concise AI assistant."
            : systemPrompt;

        var formattedPrompt = ChatPromptFormatter.Format(targetModel.PromptTemplate, finalSystemPrompt, userPrompt);

        await _governor.AcquireModelAsync(targetModel, ct).ConfigureAwait(false);
        try
        {
            await foreach (var token in _textService.GenerateStreamingAsync(targetModel, formattedPrompt, ct, imageBase64Contents).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            _governor.ReleaseModel(targetModel);
        }
    }

    private async Task<AIModelManifest?> SelectVisionModelWithProgressAsync(CancellationToken ct, IProgress<string>? pipelineStatus)
    {
        pipelineStatus?.Report(ChatPipelineStatusKeys.Processing);
        return await SelectVisionModelAsync(ct).ConfigureAwait(false);
    }

    private async Task<AIModelManifest?> SelectVisionModelAsync(CancellationToken ct)
    {
        var modelsEnumerable = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
        var low = modelsEnumerable.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Low);
        return low ?? modelsEnumerable.FirstOrDefault(m => m.Type == AIModelType.Text);
    }

    /// <param name="routingUserMessage">When non-null, used for routing and simple-prompt detection only; <paramref name="userPrompt"/> is still the full prompt for generation elsewhere.</param>
    private async Task<AIModelManifest?> SelectModelAsync(string userPrompt, CancellationToken ct, string? routingUserMessage = null, IProgress<string>? pipelineStatus = null)
    {
        var routingInput = string.IsNullOrEmpty(routingUserMessage) ? userPrompt : routingUserMessage;

        if (TryConsumePrefetchRoutingCache(routingInput, out var cached))
        {
            _logger.Debug("AIOrchestrator", "Using cached prefetch routing result.");
            pipelineStatus?.Report(ChatPipelineStatusKeys.Processing);
            return cached;
        }

        var resolution = await ResolveRoutingTargetModelAsync(routingInput, ct, pipelineStatus).ConfigureAwait(false);
        return resolution.Model;
    }

    private bool TryConsumePrefetchRoutingCache(string routingInput, out AIModelManifest? model)
    {
        lock (_selectModelLock)
        {
            model = null;
            if (_prefetchModel == null || _prefetchRoutingKey == null)
                return false;
            if (DateTime.UtcNow - _prefetchUtc > PrefetchRoutingCacheTtl)
                return false;
            if (!IsPrefetchRoutingKeyCompatible(routingInput, _prefetchRoutingKey))
                return false;
            model = _prefetchModel;
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

    private void SetPrefetchRoutingCache(string routingInput, AIModelManifest? model)
    {
        lock (_selectModelLock)
        {
            _prefetchRoutingKey = routingInput;
            _prefetchModel = model;
            _prefetchUtc = DateTime.UtcNow;
        }
    }

    private async Task<RoutingResolution> ResolveRoutingTargetModelAsync(string routingInput, CancellationToken ct, IProgress<string>? pipelineStatus)
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
            return new RoutingResolution
            {
                Model = models?.FirstOrDefault(m => m.Type == AIModelType.Text),
                RoutingComplexity = RoutingComplexity.Simple
            };
        }

        pipelineStatus?.Report(ChatPipelineStatusKeys.Routing);
        RoutingDecision? decision = null;
        string? lastRoutingError = null;
        for (var attempt = 1; attempt <= MaxRoutingAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var routeResult = await _orchestrationLayer.RouteUserMessageAsync(routingInput, ct).ConfigureAwait(false);
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

        if (decision == null)
        {
            _logger.Error(
                "AIOrchestrator",
                $"Routing failed after {MaxRoutingAttempts} attempts: {lastRoutingError ?? "unknown error"}; using low-tier model.");
            pipelineStatus?.Report(ChatPipelineStatusKeys.Processing);
            return new RoutingResolution
            {
                Model = lowModel,
                RoutingComplexity = RoutingComplexity.Simple,
                ManagerConfidence = null
            };
        }
        var hardware = _hardwareDetector.Detect();
        var tier = _hardwareTierEvaluator.EvaluateTier(hardware);

        _logger.Info("AIOrchestrator", $"Orchestration routing: complexity={decision.Complexity}, hardwareTier={tier}, confidence={decision.Confidence}, reason={decision.Reason}");

        pipelineStatus?.Report(ChatPipelineStatusKeys.Processing);

        if (decision.Complexity == RoutingComplexity.Simple)
        {
            return new RoutingResolution
            {
                Model = lowModel,
                RoutingComplexity = RoutingComplexity.Simple,
                ManagerConfidence = decision.Confidence
            };
        }

        var target = tier switch
        {
            HardwarePerformanceTier.Low => lowModel,
            HardwarePerformanceTier.Mid => midModel ?? lowModel,
            HardwarePerformanceTier.High => highModel ?? midModel ?? lowModel,
            _ => lowModel
        };

        return new RoutingResolution
        {
            Model = target,
            RoutingComplexity = RoutingComplexity.Reasoning,
            ManagerConfidence = decision.Confidence
        };
    }

    public async Task PrefetchRoutingAndWarmupAsync(string routingUserMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routingUserMessage))
            return;

        var resolution = await ResolveRoutingTargetModelAsync(routingUserMessage, ct, pipelineStatus: null).ConfigureAwait(false);
        if (resolution.Model == null)
            return;

        if (resolution.RoutingComplexity == RoutingComplexity.Reasoning
            && string.Equals(resolution.ManagerConfidence, "low", StringComparison.OrdinalIgnoreCase)
            && IsHeavyChatModel(resolution.Model))
        {
            _logger.Debug("AIOrchestrator", "Skipping prefetch routing cache and warm-up (manager confidence low for reasoning; would load mid/high tier).");
            return;
        }

        SetPrefetchRoutingCache(routingUserMessage, resolution.Model);

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

    private readonly struct RoutingResolution
    {
        public AIModelManifest? Model { get; init; }
        public RoutingComplexity RoutingComplexity { get; init; }
        public string? ManagerConfidence { get; init; }
    }

    public async Task WarmUpLowTierModelAsync(CancellationToken ct = default)
    {
        var models = (await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false)).ToList();
        var manager = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Manager);
        var lowModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Low);

        if (manager != null)
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

        if (lowModel == null)
        {
            return;
        }

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
        var targetModel = await SelectModelAsync(userPrompt, ct).ConfigureAwait(false);
        if (targetModel == null) return Result<string>.Failure("No suitable text model found.");

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo, a helpful AI assistant."
            : systemPrompt;

        var formattedPrompt = ChatPromptFormatter.Format(targetModel.PromptTemplate, effectiveSystemPrompt, userPrompt);
        return await PromptWithModelAsync(targetModel.Id, formattedPrompt, ct, responseJsonSchema).ConfigureAwait(false);
    }
}
