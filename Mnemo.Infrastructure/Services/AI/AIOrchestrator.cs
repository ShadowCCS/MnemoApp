using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public class AIOrchestrator : IAIOrchestrator
{
    private readonly IAIModelRegistry _modelRegistry;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly IResourceGovernor _governor;
    private readonly ITextGenerationService _textService;

    public AIOrchestrator(
        IAIModelRegistry modelRegistry,
        IKnowledgeService knowledgeService,
        ISettingsService settings,
        ILoggerService logger,
        IResourceGovernor governor,
        ITextGenerationService textService)
    {
        _modelRegistry = modelRegistry;
        _knowledgeService = knowledgeService;
        _settings = settings;
        _logger = logger;
        _governor = governor;
        _textService = textService;
    }

    public async Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        // Smart RAG: Only search if enabled and query is substantial enough
        var smartRagEnabled = await _settings.GetAsync("AI.SmartRAG", true).ConfigureAwait(false);
        
        if (smartRagEnabled && ShouldUseRag(userPrompt))
        {
            var contextResult = await _knowledgeService.SearchAsync(userPrompt, 3, ct).ConfigureAwait(false);
            if (contextResult.IsSuccess && contextResult.Value != null)
            {
                var relevantChunks = contextResult.Value.Where(c => c.RelevanceScore > 0.6).ToList();
                if (relevantChunks.Any())
                {
                    _logger.Info("AIOrchestrator", $"Using RAG with {relevantChunks.Count} chunks (best score: {relevantChunks.Max(c => c.RelevanceScore):F2})");
                    return await PromptWithContextAsync(userPrompt, relevantChunks, ct).ConfigureAwait(false);
                }
            }
        }

        return await ExecutePromptAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
    }

    private static bool ShouldUseRag(string query)
    {
        // Skip RAG for short queries (greetings, simple questions)
        if (query.Length < 20) return false;
        
        // Skip RAG for obvious greetings/chitchat
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
        // Phase A: Smart Query Expansion/Rewriting
        var searchPrompt = await RewriteQueryAsync(prompt, ct).ConfigureAwait(false);
        
        // Phase B: Smart condensing
        var condensedContext = await CondenseContextAsync(context, searchPrompt, ct).ConfigureAwait(false);

        // Phase C: Answer
        var systemPrompt = "You are Mnemo, an expert assistant. Use the provided context to answer the user's question accurately. If the answer is not in the context, politely state that you don't have that information. Keep answers professional and grounded in the provided facts.";
        var fullPrompt = $"Context:\n{condensedContext}\n\nQuestion: {prompt}";

        return await ExecutePromptAsync(systemPrompt, fullPrompt, ct).ConfigureAwait(false);
    }

    private async Task<string> RewriteQueryAsync(string query, CancellationToken ct)
    {
        // Only rewrite if query is long/complex enough to benefit
        if (query.Length < 30) return query;

        var rewritePrompt = $"""
        <|im_start|>system
        Rewrite the user's query into a better search term for a vector database. Extract key concepts and entities. Respond ONLY with the rewritten query.<|im_end|>
        <|im_start|>user
        {query}<|im_end|>
        <|im_start|>assistant
        """;

        var models = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
        var fastModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && !m.IsOptional);
        
        if (fastModel == null) return query;

        var result = await _textService.GenerateAsync(fastModel, rewritePrompt, ct).ConfigureAwait(false);
        return result.IsSuccess ? result.Value?.Trim() ?? query : query;
    }

    private async Task<string> CondenseContextAsync(IEnumerable<KnowledgeChunk> context, string query, CancellationToken ct)
    {
        await Task.Yield();
        
        // Sort by relevance and take top chunks. 
        // With 128k context support, we can afford to send many more chunks.
        var relevantChunks = context
            .OrderByDescending(c => c.RelevanceScore)
            .Take(50) // Increased from 10 to 50 to leverage long context
            .ToList();

        if (!relevantChunks.Any()) return string.Empty;

        var sb = new StringBuilder();
        foreach (var chunk in relevantChunks)
        {
            sb.AppendLine($"--- Source: {chunk.Metadata.GetValueOrDefault("path", "Unknown")} ---");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    public async Task<Result<string>> PromptWithModelAsync(string modelId, string prompt, CancellationToken ct = default)
    {
        var manifest = await _modelRegistry.GetModelAsync(modelId).ConfigureAwait(false);
        if (manifest == null) return Result<string>.Failure("Model not found.");

        await _governor.AcquireModelAsync(manifest, ct).ConfigureAwait(false);
        try
        {
            _logger.Info("AIOrchestrator", $"Executing prompt with model: {manifest.DisplayName}");
            return await _textService.GenerateAsync(manifest, prompt, ct).ConfigureAwait(false);
        }
        finally
        {
            _governor.ReleaseModel(manifest);
        }
    }

    public async IAsyncEnumerable<string> PromptStreamingAsync(string systemPrompt, string userPrompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // For simplicity in the first version, we skip RAG for streaming if it requires complex rewriting,
        // but we can still use the basic user prompt.
        var targetModel = await SelectModelAsync(userPrompt, ct).ConfigureAwait(false);
        if (targetModel == null) yield break;

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? "You are Mnemo, a helpful and concise AI assistant."
            : systemPrompt;

        var formattedPrompt = FormatPrompt(targetModel.PromptTemplate, effectiveSystemPrompt, userPrompt);

        await _governor.AcquireModelAsync(targetModel, ct).ConfigureAwait(false);
        try
        {
            await foreach (var token in _textService.GenerateStreamingAsync(targetModel, formattedPrompt, ct).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            _governor.ReleaseModel(targetModel);
        }
    }

    private async Task<AIModelManifest?> SelectModelAsync(string userPrompt, CancellationToken ct)
    {
        bool smartSwitchEnabled = await _settings.GetAsync("AI.SmartSwitch", false).ConfigureAwait(false);
        var models = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);

        var fastModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && !m.IsOptional);
        var smartModel = models.FirstOrDefault(m => m.Type == AIModelType.Text && m.IsOptional);

        AIModelManifest? targetModel = null;

        if (smartSwitchEnabled && smartModel != null && fastModel != null)
        {
            if (IsObviouslySimple(userPrompt))
            {
                targetModel = fastModel;
            }
            else
            {
                var routingResult = await RouteRequestAsync(fastModel, userPrompt, ct).ConfigureAwait(false);
                targetModel = routingResult == "COMPLEX" ? smartModel : fastModel;
                _logger.Info("AIOrchestrator", $"AI Router decided: {routingResult} -> using {targetModel.DisplayName}");
            }
        }
        else
        {
            targetModel = fastModel;
        }

        return targetModel ?? models.FirstOrDefault(m => m.Type == AIModelType.Text);
    }

    private async Task<Result<string>> ExecutePromptAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var targetModel = await SelectModelAsync(userPrompt, ct).ConfigureAwait(false);

        if (targetModel == null) return Result<string>.Failure("No suitable text model found.");

        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) 
            ? "You are Mnemo, a helpful and concise AI assistant." 
            : systemPrompt;

        var formattedPrompt = FormatPrompt(targetModel.PromptTemplate, effectiveSystemPrompt, userPrompt);
        
        return await PromptWithModelAsync(targetModel.Id, formattedPrompt, ct).ConfigureAwait(false);
    }

    private string FormatPrompt(string template, string system, string user)
    {
        return template.ToUpperInvariant() switch
        {
            "CHATML" => $"""
                <|im_start|>system
                {system}<|im_end|>
                <|im_start|>user
                {user}<|im_end|>
                <|im_start|>assistant

                """,
            "LLAMA3" => $"""
                <|begin_of_text|><|start_header_id|>system<|end_header_id|>

                {system}<|eot_id|><|start_header_id|>user<|end_header_id|>

                {user}<|eot_id|><|start_header_id|>assistant<|end_header_id|>

                """,
            "ALPACA" => $"""
                ### Instruction:
                {system}

                {user}

                ### Response:
                """,
            "VICUNA" => $"""
                {system}

                USER: {user}
                ASSISTANT:
                """,
            _ => $"""
                System: {system}
                User: {user}
                Assistant:
                """
        };
    }

    private async Task<string> RouteRequestAsync(AIModelManifest routerModel, string userPrompt, CancellationToken ct)
    {
        var routingPrompt = $"""
<|im_start|>system
Analyze the user request and classify it as 'SIMPLE' or 'COMPLEX'. Respond with ONLY the word 'SIMPLE' or 'COMPLEX'.<|im_end|>
<|im_start|>user
{userPrompt}<|im_end|>
<|im_start|>assistant
""";
        
        var result = await PromptWithModelAsync(routerModel.Id, routingPrompt, ct).ConfigureAwait(false);
        if (!result.IsSuccess) return "SIMPLE";

        var classification = result.Value?.Trim().ToUpperInvariant() ?? "SIMPLE";
        return classification.Contains("COMPLEX") ? "COMPLEX" : "SIMPLE";
    }

    private static bool IsObviouslySimple(string query)
    {
        if (query.Length < 10) return true;

        var simpleGreetings = new[] { "hi", "hello", "hey", "how are you", "what's up", "thanks", "thank you", "bye", "goodbye" };
        var lowerQuery = query.ToLowerInvariant().Trim();
        return simpleGreetings.Any(g => lowerQuery.Equals(g) || lowerQuery.StartsWith(g + " "));
    }
}
