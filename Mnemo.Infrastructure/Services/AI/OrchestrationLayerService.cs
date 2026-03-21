using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Invokes the always-on mini model (<see cref="AIModelRoles.Manager"/>) with TaskType prompts and optional batching.
/// </summary>
public sealed class OrchestrationLayerService : IOrchestrationLayer
{
    private readonly IAIModelRegistry _modelRegistry;
    private readonly ITextGenerationService _textService;
    private readonly IResourceGovernor _governor;
    private readonly ILoggerService _logger;

    public OrchestrationLayerService(
        IAIModelRegistry modelRegistry,
        ITextGenerationService textService,
        IResourceGovernor governor,
        ILoggerService logger)
    {
        _modelRegistry = modelRegistry;
        _textService = textService;
        _governor = governor;
        _logger = logger;
    }

    public async Task<Result<RoutingDecision>> RouteUserMessageAsync(string userMessage, CancellationToken ct = default)
    {
        var manager = await GetManagerManifestAsync().ConfigureAwait(false);
        if (manager == null)
            return Result<RoutingDecision>.Failure("Orchestration model is not available.");

        var userBlock = BuildTaskUserContent(OrchestrationTaskTypes.Routing, userMessage);
        var prompt = ChatPromptFormatter.Format(manager.PromptTemplate, string.Empty, userBlock);

        await _governor.AcquireModelAsync(manager, ct).ConfigureAwait(false);
        try
        {
            var gen = await _textService.GenerateAsync(manager, prompt, ct).ConfigureAwait(false);
            if (!gen.IsSuccess || gen.Value == null)
                return Result<RoutingDecision>.Failure(gen.ErrorMessage ?? "Routing generation failed.", gen.Exception);

            var parsed = RoutingResponseParser.TryParse(gen.Value);
            if (parsed == null)
            {
                _logger.Warning("OrchestrationLayer", $"Routing JSON parse failed; raw: {Truncate(gen.Value, 200)}");
                return Result<RoutingDecision>.Failure("Could not parse routing response.");
            }

            _logger.Debug(
                "OrchestrationLayer",
                $"Manager routing decision: complexity={parsed.Complexity}, confidence={parsed.Confidence}, reason={parsed.Reason}");

            return Result<RoutingDecision>.Success(parsed);
        }
        finally
        {
            _governor.ReleaseModel(manager);
        }
    }

    public async Task<Result<IReadOnlyList<OrchestrationTaskResult>>> RunTasksAsync(
        IReadOnlyList<OrchestrationTaskRequest> requests,
        OrchestrationExecutionMode executionMode,
        CancellationToken ct = default)
    {
        if (requests == null || requests.Count == 0)
            return Result<IReadOnlyList<OrchestrationTaskResult>>.Success(Array.Empty<OrchestrationTaskResult>());

        if (executionMode == OrchestrationExecutionMode.Parallel)
        {
            var tasks = requests.Select(r => RunSingleTaskAsync(r, ct)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return Result<IReadOnlyList<OrchestrationTaskResult>>.Success(results);
        }

        var list = new List<OrchestrationTaskResult>(requests.Count);
        foreach (var r in requests)
        {
            list.Add(await RunSingleTaskAsync(r, ct).ConfigureAwait(false));
        }

        return Result<IReadOnlyList<OrchestrationTaskResult>>.Success(list);
    }

    private async Task<OrchestrationTaskResult> RunSingleTaskAsync(OrchestrationTaskRequest request, CancellationToken ct)
    {
        var manager = await GetManagerManifestAsync().ConfigureAwait(false);
        if (manager == null)
        {
            return new OrchestrationTaskResult
            {
                TaskType = request.TaskType,
                IsSuccess = false,
                ErrorMessage = "Orchestration model is not available."
            };
        }

        var userBlock = BuildTaskUserContent(request.TaskType, request.UserMessage);
        var prompt = ChatPromptFormatter.Format(manager.PromptTemplate, string.Empty, userBlock);

        await _governor.AcquireModelAsync(manager, ct).ConfigureAwait(false);
        try
        {
            var gen = await _textService.GenerateAsync(manager, prompt, ct).ConfigureAwait(false);
            if (!gen.IsSuccess || gen.Value == null)
            {
                return new OrchestrationTaskResult
                {
                    TaskType = request.TaskType,
                    IsSuccess = false,
                    ErrorMessage = gen.ErrorMessage
                };
            }

            return new OrchestrationTaskResult
            {
                TaskType = request.TaskType,
                RawContent = gen.Value,
                IsSuccess = true
            };
        }
        finally
        {
            _governor.ReleaseModel(manager);
        }
    }

    private async Task<AIModelManifest?> GetManagerManifestAsync()
    {
        var models = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
        return models.FirstOrDefault(m => m.Type == AIModelType.Text && m.Role == AIModelRoles.Manager);
    }

    private static string BuildTaskUserContent(string taskType, string userMessage)
    {
        var sb = new StringBuilder();
        sb.Append("TaskType: ").Append(taskType).Append('\n');
        sb.Append("[USER MESSAGE]: ").Append(userMessage);
        return sb.ToString();
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";
}
