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
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISettingsService _settings;
    private readonly IChatDatasetLogger _chatDatasetLogger;
    private readonly ITeacherModelClient _teacherClient;

    public OrchestrationLayerService(
        IAIModelRegistry modelRegistry,
        ITextGenerationService textService,
        IResourceGovernor governor,
        ILoggerService logger,
        ISkillRegistry skillRegistry,
        ISettingsService settings,
        IChatDatasetLogger chatDatasetLogger,
        ITeacherModelClient teacherClient)
    {
        _modelRegistry = modelRegistry;
        _textService = textService;
        _governor = governor;
        _logger = logger;
        _skillRegistry = skillRegistry;
        _settings = settings;
        _chatDatasetLogger = chatDatasetLogger;
        _teacherClient = teacherClient;
    }

    public async Task<Result<RoutingAndSkillDecision>> RouteAndClassifySkillAsync(
        string userMessage,
        RoutingToolHint? recentToolHint = null,
        CancellationToken ct = default,
        ConversationMemorySnapshot? memorySnapshot = null)
    {
        await _skillRegistry.LoadAsync(ct).ConfigureAwait(false);
        var enabledSkillsEarly = _skillRegistry.GetEnabledSkills();
        var skillIdsEarly = enabledSkillsEarly.Select(s => s.Id).ToList();
        var userBlockEarly = RoutingAndSkillPromptBuilder.Build(userMessage, enabledSkillsEarly, recentToolHint, memorySnapshot);

        if (await _settings.GetAsync(TeacherModelSettings.UseTeacherRoutingKey, false).ConfigureAwait(false)
            && await _teacherClient.IsConfiguredAsync(ct).ConfigureAwait(false))
        {
            var teacherGen = await _teacherClient.GenerateRoutingDecisionJsonAsync(userBlockEarly, ct).ConfigureAwait(false);
            if (teacherGen.IsSuccess && teacherGen.Value != null)
            {
                var parsedTeacher = RoutingAndSkillResponseParser.TryParse(teacherGen.Value);
                if (parsedTeacher != null)
                {
                    _logger.Debug(
                        "OrchestrationLayer",
                        $"Teacher routing+skill decision: complexity={parsedTeacher.Complexity}, skills=[{string.Join(", ", parsedTeacher.Skills)}], confidence={parsedTeacher.Confidence}, reason={parsedTeacher.Reason}");

                    await TryStageManagerDatasetAsync(
                        ct,
                        () => new ChatDatasetManagerSection
                        {
                            ModelId = TeacherSyntheticManifest.ChatModelId,
                            ModelDisplayName = "Gemini 2.5 Flash (teacher)",
                            EnabledSkillIds = skillIdsEarly,
                            RoutingModelInput = RoutingModelInputSnapshot(
                                userBlockEarly,
                                "vertex_teacher",
                                TeacherRoutingPrompts.SystemInstruction,
                                localManagerFullPrompt: null),
                            UserBlock = userBlockEarly,
                            RoutingProvider = "vertex_teacher",
                            RoutingSystemInstruction = TeacherRoutingPrompts.SystemInstruction,
                            FullPrompt = "",
                            ResponseRaw = teacherGen.Value,
                            Success = true,
                            ParsedDecision = new ChatDatasetRoutingDecision
                            {
                                Complexity = parsedTeacher.Complexity.ToString(),
                                Skills = parsedTeacher.Skills,
                                Confidence = parsedTeacher.Confidence,
                                Reason = parsedTeacher.Reason
                            }
                        }).ConfigureAwait(false);

                    return Result<RoutingAndSkillDecision>.Success(parsedTeacher);
                }
            }

            _logger.Warning("OrchestrationLayer", "Teacher routing failed or produced invalid JSON; falling back to local manager model.");
        }

        var manager = await GetManagerManifestAsync().ConfigureAwait(false);
        if (manager == null)
        {
            await TryStageManagerDatasetAsync(
                ct,
                () => new ChatDatasetManagerSection
                {
                    RoutingModelInput = RoutingModelInputSnapshot(
                        userBlockEarly,
                        "local_manager",
                        vertexSystemInstruction: null,
                        localManagerFullPrompt: null),
                    UserBlock = userBlockEarly,
                    Success = false,
                    Error = "Orchestration model is not available.",
                    FullPrompt = "",
                    RoutingProvider = "local_manager"
                }).ConfigureAwait(false);
            return Result<RoutingAndSkillDecision>.Failure("Orchestration model is not available.");
        }

        var skillIds = skillIdsEarly;
        var userBlock = userBlockEarly;
        var prompt = ChatPromptFormatter.Format(manager.PromptTemplate, string.Empty, userBlock);

        await _governor.AcquireModelAsync(manager, ct).ConfigureAwait(false);
        try
        {
            var gen = await _textService.GenerateAsync(manager, prompt, ct).ConfigureAwait(false);
            if (!gen.IsSuccess || gen.Value == null)
            {
                await TryStageManagerDatasetAsync(
                    ct,
                    () => new ChatDatasetManagerSection
                    {
                        ModelId = manager.Id,
                        ModelDisplayName = manager.DisplayName,
                        EnabledSkillIds = skillIds,
                        RoutingModelInput = RoutingModelInputSnapshot(
                            userBlock,
                            "local_manager",
                            vertexSystemInstruction: null,
                            localManagerFullPrompt: prompt),
                        UserBlock = userBlock,
                        RoutingProvider = "local_manager",
                        FullPrompt = prompt,
                        Success = false,
                        Error = gen.ErrorMessage ?? "Routing and skill generation failed.",
                        ResponseRaw = null
                    }).ConfigureAwait(false);
                return Result<RoutingAndSkillDecision>.Failure(gen.ErrorMessage ?? "Routing and skill generation failed.", gen.Exception);
            }

            var parsed = RoutingAndSkillResponseParser.TryParse(gen.Value);
            if (parsed == null)
            {
                _logger.Warning("OrchestrationLayer", $"Routing+skill JSON parse failed; raw: {Truncate(gen.Value, 200)}");
                await TryStageManagerDatasetAsync(
                    ct,
                    () => new ChatDatasetManagerSection
                    {
                        ModelId = manager.Id,
                        ModelDisplayName = manager.DisplayName,
                        EnabledSkillIds = skillIds,
                        RoutingModelInput = RoutingModelInputSnapshot(
                            userBlock,
                            "local_manager",
                            vertexSystemInstruction: null,
                            localManagerFullPrompt: prompt),
                        UserBlock = userBlock,
                        RoutingProvider = "local_manager",
                        FullPrompt = prompt,
                        ResponseRaw = gen.Value,
                        Success = false,
                        Error = "Could not parse routing and skill response."
                    }).ConfigureAwait(false);
                return Result<RoutingAndSkillDecision>.Failure("Could not parse routing and skill response.");
            }

            _logger.Debug(
                "OrchestrationLayer",
                $"Manager routing+skill decision: complexity={parsed.Complexity}, skills=[{string.Join(", ", parsed.Skills)}], confidence={parsed.Confidence}, reason={parsed.Reason}");

            await TryStageManagerDatasetAsync(
                ct,
                () => new ChatDatasetManagerSection
                {
                    ModelId = manager.Id,
                    ModelDisplayName = manager.DisplayName,
                    EnabledSkillIds = skillIds,
                    RoutingModelInput = RoutingModelInputSnapshot(
                        userBlock,
                        "local_manager",
                        vertexSystemInstruction: null,
                        localManagerFullPrompt: prompt),
                    UserBlock = userBlock,
                    RoutingProvider = "local_manager",
                    FullPrompt = prompt,
                    ResponseRaw = gen.Value,
                    Success = true,
                    ParsedDecision = new ChatDatasetRoutingDecision
                    {
                        Complexity = parsed.Complexity.ToString(),
                        Skills = parsed.Skills,
                        Confidence = parsed.Confidence,
                        Reason = parsed.Reason
                    }
                }).ConfigureAwait(false);

            return Result<RoutingAndSkillDecision>.Success(parsed);
        }
        finally
        {
            _governor.ReleaseModel(manager);
        }
    }

    private async Task TryStageManagerDatasetAsync(CancellationToken ct, Func<ChatDatasetManagerSection> build)
    {
        var turnId = ChatDatasetLoggingScope.CurrentTurnId;
        if (string.IsNullOrEmpty(turnId)) return;
        if (!await _settings.GetAsync(ChatDatasetSettings.LoggingEnabledKey, false).ConfigureAwait(false)) return;
        await _chatDatasetLogger.StageManagerAsync(turnId, build(), ct).ConfigureAwait(false);
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

        // convo_summarize passes the full frozen prompt from ConvoSummarizePromptBuilder; do not wrap again.
        var userBlock = string.Equals(request.TaskType, OrchestrationTaskTypes.ConvoSummarize, StringComparison.Ordinal)
            ? request.UserMessage
            : BuildTaskUserContent(request.TaskType, request.UserMessage);
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

    /// <summary>
    /// Single place describing the routing model input: Vertex = system + user task block; local = full prompt string.
    /// </summary>
    private static ChatDatasetRoutingModelInput RoutingModelInputSnapshot(
        string userTaskBlock,
        string routingProvider,
        string? vertexSystemInstruction,
        string? localManagerFullPrompt)
    {
        return new ChatDatasetRoutingModelInput
        {
            UserTaskBlock = userTaskBlock,
            SystemInstruction = string.Equals(routingProvider, "vertex_teacher", StringComparison.Ordinal)
                ? vertexSystemInstruction
                : null,
            LocalManagerFullPrompt = string.Equals(routingProvider, "local_manager", StringComparison.Ordinal)
                ? localManagerFullPrompt
                : null
        };
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

}
