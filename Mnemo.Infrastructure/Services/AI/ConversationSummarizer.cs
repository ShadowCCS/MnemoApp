using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Produces a rolling <see cref="ConversationSummary"/> by submitting a
/// <c>convo_summarize</c> task to the manager model via <see cref="IOrchestrationLayer"/>.
/// </summary>
public sealed class ConversationSummarizer : IConversationSummarizer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IOrchestrationLayer _orchestration;
    private readonly ILoggerService _logger;

    public ConversationSummarizer(IOrchestrationLayer orchestration, ILoggerService logger)
    {
        _orchestration = orchestration;
        _logger = logger;
    }

    public async Task<Result<ConversationSummary>> SummarizeAsync(
        ConversationMemorySnapshot snapshot,
        IReadOnlyList<ConversationTurn> newTurnsSinceLastSummary,
        CancellationToken ct = default)
    {
        if (snapshot == null)
            return Result<ConversationSummary>.Failure("Snapshot is required.");

        var prompt = ConvoSummarizePromptBuilder.Build(snapshot, newTurnsSinceLastSummary);

        var request = new OrchestrationTaskRequest
        {
            TaskType = OrchestrationTaskTypes.ConvoSummarize,
            UserMessage = prompt
        };

        var result = await _orchestration.RunTasksAsync(
            [request],
            OrchestrationExecutionMode.Sequential,
            ct).ConfigureAwait(false);

        if (!result.IsSuccess || result.Value == null || result.Value.Count == 0)
            return Result<ConversationSummary>.Failure(result.ErrorMessage ?? "Summarization task failed.");

        var taskResult = result.Value[0];
        if (!taskResult.IsSuccess || string.IsNullOrWhiteSpace(taskResult.RawContent))
            return Result<ConversationSummary>.Failure(taskResult.ErrorMessage ?? "No summary content returned.");

        var parsed = TryParseResponse(taskResult.RawContent, snapshot.TurnCount);
        if (parsed == null)
        {
            _logger.Warning("Memory",
                $"Summarizer: JSON parse failed; raw: {Truncate(taskResult.RawContent, 300)}");
            return Result<ConversationSummary>.Failure("Could not parse summary JSON from manager model.");
        }

        _logger.Info("Memory",
            $"Summarizer: ok turn={parsed.CoveredThroughTurn} active_skill={parsed.ActiveSkill} entities={parsed.ActiveEntities.Count}");

        return Result<ConversationSummary>.Success(parsed);
    }

    private static ConversationSummary? TryParseResponse(string raw, int currentTurn)
    {
        var json = ExtractJson(raw.Trim());
        if (json == null)
            return null;

        try
        {
            var dto = JsonSerializer.Deserialize<SummaryDto>(json, JsonOpts);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Summary))
                return null;

            return new ConversationSummary
            {
                Summary = dto.Summary.Trim(),
                ActiveEntities = dto.ActiveEntities ?? new(),
                KeyFacts = dto.KeyFacts ?? new(),
                ActiveSkill = string.IsNullOrWhiteSpace(dto.ActiveSkill) ? "NONE" : dto.ActiveSkill.Trim(),
                CoveredThroughTurn = currentTurn,
                CreatedUtc = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the JSON object from a raw model response that may include leading/trailing prose.
    /// </summary>
    private static string? ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return raw[start..(end + 1)];
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

    private sealed class SummaryDto
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("active_entities")]
        public Dictionary<string, string>? ActiveEntities { get; set; }

        [JsonPropertyName("key_facts")]
        public List<string>? KeyFacts { get; set; }

        [JsonPropertyName("active_skill")]
        public string? ActiveSkill { get; set; }
    }
}
