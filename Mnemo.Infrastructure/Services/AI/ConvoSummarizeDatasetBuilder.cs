using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Generates <c>convo_summarize</c> prompt/completion pairs from conversation seeds using a
/// teacher model for labeling. Each pair is validated for entity ID preservation before being
/// accepted into the dataset.
///
/// Seed sources (fed by caller):
/// <list type="bullet">
///   <item>Real conversations from <c>conversations.jsonl</c> via <see cref="ChatDatasetTurnRecord"/> logs.</item>
///   <item>Synthetically authored seeds for coverage of edge cases.</item>
/// </list>
/// </summary>
public sealed class ConvoSummarizeDatasetBuilder
{
    private readonly ITeacherModelClient _teacher;
    private readonly ILoggerService _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Teacher system instruction for the <c>convo_summarize</c> labeling task.
    /// This prompt is frozen: changing it requires regenerating training data.
    /// </summary>
    public const string TeacherSystemInstruction =
        "You are generating training data for a conversation summarizer. " +
        "Given a conversation below (including tool usage traces), produce a JSON summary " +
        "following this schema exactly:\n" +
        "{ \"summary\": \"<≤3 sentence prose summary>\", " +
        "\"active_entities\": { \"<type>\": \"<id>\" }, " +
        "\"key_facts\": [\"<short phrase under 10 words>\"], " +
        "\"active_skill\": \"<Notes|Mindmap|Application|Settings|NONE>\" }\n\n" +
        "Rules:\n" +
        "- Preserve ALL entity IDs (note_id, mindmap_id, node_id, block_id) that are still relevant.\n" +
        "- active_entities keys must be typed: note_id, mindmap_id, node_id, block_id.\n" +
        "- key_facts are short phrases only (under 10 words each).\n" +
        "- summary must read as if written for a new assistant taking over mid-session.\n" +
        "- Never hallucinate IDs. Only use IDs that explicitly appeared in tool results.\n" +
        "- active_skill: the skill most recently in use (Notes, Mindmap, Application, Settings, or NONE).";

    public ConvoSummarizeDatasetBuilder(ITeacherModelClient teacher, ILoggerService logger)
    {
        _teacher = teacher;
        _logger = logger;
    }

    /// <summary>
    /// Generates a dataset example from a seed conversation. Returns null when the teacher
    /// produces an invalid response or the entity ID validation fails.
    /// </summary>
    /// <param name="seed">Formatted seed containing prior summary + new turns + working memory.</param>
    /// <param name="knownEntityIds">
    /// All entity IDs that appear in the seed and must survive in the output <c>active_entities</c>.
    /// Examples from tool results: note IDs, mindmap IDs.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DatasetSummarizationExample?> GenerateExampleAsync(
        ConvoSummarizeSeed seed,
        IReadOnlyList<string> knownEntityIds,
        CancellationToken ct = default)
    {
        var prompt = BuildPromptFromSeed(seed);
        var teacherResponse = await _teacher.GenerateTextAsync(
            TeacherSystemInstruction,
            prompt,
            ct).ConfigureAwait(false);

        if (!teacherResponse.IsSuccess || string.IsNullOrWhiteSpace(teacherResponse.Value))
        {
            _logger.Warning("ConvoSummarizeDatasetBuilder",
                $"Teacher generation failed: {teacherResponse.ErrorMessage}");
            return null;
        }

        var completion = NormalizeCompletion(teacherResponse.Value);
        if (!ValidateEntityIds(completion, knownEntityIds))
        {
            _logger.Debug("ConvoSummarizeDatasetBuilder",
                "Discarding example: entity ID dropped in teacher completion.");
            return null;
        }

        return new DatasetSummarizationExample
        {
            Prompt = prompt,
            Completion = completion,
            SeedType = seed.SeedType,
            ConversationId = seed.ConversationId
        };
    }

    private static string BuildPromptFromSeed(ConvoSummarizeSeed seed)
    {
        var sb = new StringBuilder();
        sb.Append("TaskType: ").AppendLine(OrchestrationTaskTypes.ConvoSummarize);
        sb.Append("[PREVIOUS SUMMARY]: ").AppendLine(
            string.IsNullOrWhiteSpace(seed.PreviousSummary) ? "None" : seed.PreviousSummary);
        sb.AppendLine("[NEW TURNS]:");
        sb.AppendLine(seed.TurnsText.Trim());
        sb.AppendLine("[WORKING MEMORY]:");
        sb.AppendLine(string.IsNullOrWhiteSpace(seed.WorkingMemoryText) ? "None" : seed.WorkingMemoryText.Trim());
        ConvoSummarizePromptBuilder.AppendConvoSummarizeInstructions(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Validates that all <paramref name="expectedIds"/> that appear in the teacher's output
    /// are present in the <c>active_entities</c> map. Returns true when no IDs were dropped.
    /// </summary>
    private static bool ValidateEntityIds(string completionJson, IReadOnlyList<string> expectedIds)
    {
        if (expectedIds.Count == 0)
            return true;

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(completionJson, JsonOpts);
            if (!root.TryGetProperty("active_entities", out var entities))
                return expectedIds.Count == 0;

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in entities.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    values.Add(prop.Value.GetString() ?? string.Empty);
            }

            return expectedIds.All(id => values.Contains(id));
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeCompletion(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            if (s.EndsWith("```", StringComparison.Ordinal))
                s = s[..^3].TrimEnd();
        }
        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }
}

/// <summary>Seed input for a single <c>convo_summarize</c> dataset example.</summary>
public sealed class ConvoSummarizeSeed
{
    public string? ConversationId { get; init; }
    public string? PreviousSummary { get; init; }
    public string TurnsText { get; init; } = string.Empty;
    public string? WorkingMemoryText { get; init; }
    public string SeedType { get; init; } = "unknown";
}

/// <summary>One <c>convo_summarize</c> training example (prompt + completion).</summary>
public sealed class DatasetSummarizationExample
{
    public string Prompt { get; init; } = string.Empty;
    public string Completion { get; init; } = string.Empty;
    public string SeedType { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
}
