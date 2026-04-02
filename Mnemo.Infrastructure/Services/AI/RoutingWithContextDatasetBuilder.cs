using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Generates <c>routing_and_skill_detection</c> training examples that include a
/// <c>[CONVERSATION CONTEXT]</c> block produced from a <see cref="ConversationSummary"/>.
///
/// These examples supplement (not replace) the existing routing dataset — both are used
/// in joint training so the manager model can handle both cold-start and warm-context routing.
///
/// Seeds are shared with <see cref="ConvoSummarizeDatasetBuilder"/>: for each conversation seed
/// the teacher generates a summary first, then routing labels for follow-up messages in that context.
/// </summary>
public sealed class RoutingWithContextDatasetBuilder
{
    private readonly ITeacherModelClient _teacher;
    private readonly ILoggerService _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Teacher system instruction for context-aware routing labeling.
    /// Frozen: changing it requires regenerating training data.
    /// </summary>
    public const string TeacherSystemInstruction =
        "You are generating training data for a conversation router.\n" +
        "Given the user message and conversation context, output the correct routing decision.\n" +
        "Output JSON only: { \"skills\": [\"<skill>\"], \"complexity\": \"simple|reasoning\", \"confidence\": 0.0-1.0, \"reason\": \"...\" }\n" +
        "Use a one-element array for a single module, e.g. [\"Notes\"] or [\"NONE\"].\n\n" +
        "Available skills: Notes, Mindmap, Application, Settings, Path, NONE\n\n" +
        "Rules:\n" +
        "- When the user's message is a short follow-up (\"yes\", \"do it\", \"add more\", \"continue\") and an active_skill is in context, choose that skill.\n" +
        "- When the user clearly changes topic or contradicts the active skill, override it with the new skill or NONE.\n" +
        "- complexity=reasoning only when the task requires multi-step reasoning or planning.\n" +
        "- confidence should reflect how certain the routing is (0.9+ for unambiguous, 0.6-0.8 for likely, below 0.6 for ambiguous).";

    public RoutingWithContextDatasetBuilder(ITeacherModelClient teacher, ILoggerService logger)
    {
        _teacher = teacher;
        _logger = logger;
    }

    /// <summary>
    /// Generates a context-aware routing example. Returns null when the teacher produces
    /// an invalid response or the skill validation fails.
    /// </summary>
    /// <param name="seed">The routing seed containing context summary and user message.</param>
    /// <param name="enabledSkills">Skill definitions available in this context.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DatasetRoutingWithContextExample?> GenerateExampleAsync(
        RoutingWithContextSeed seed,
        IReadOnlyList<SkillDefinition> enabledSkills,
        CancellationToken ct = default)
    {
        var memorySnapshot = BuildSnapshotFromSeed(seed);
        var routingHint = seed.LastToolName != null
            ? new RoutingToolHint(seed.ActiveSkill ?? "NONE", seed.LastToolName, null)
            : null;

        var prompt = RoutingAndSkillPromptBuilder.Build(
            seed.UserMessage,
            enabledSkills,
            routingHint,
            memorySnapshot);

        var teacherResponse = await _teacher.GenerateRoutingDecisionJsonAsync(
            BuildTeacherUserMessage(seed), ct).ConfigureAwait(false);

        if (!teacherResponse.IsSuccess || string.IsNullOrWhiteSpace(teacherResponse.Value))
        {
            _logger.Warning("RoutingWithContextDatasetBuilder",
                $"Teacher generation failed: {teacherResponse.ErrorMessage}");
            return null;
        }

        var completion = NormalizeCompletion(teacherResponse.Value);

        // Validate: for short follow-up seeds the teacher skill must match the active skill
        if (seed.IsShortFollowUp && seed.ActiveSkill != null)
        {
            if (!ValidateFollowUpSkill(completion, seed.ActiveSkill))
            {
                _logger.Debug("RoutingWithContextDatasetBuilder",
                    $"Discarding follow-up example: teacher chose wrong skill for active_skill={seed.ActiveSkill}.");
                return null;
            }
        }

        return new DatasetRoutingWithContextExample
        {
            Prompt = prompt,
            Completion = completion,
            SeedType = seed.SeedType,
            UserMessage = seed.UserMessage,
            ActiveSkill = seed.ActiveSkill,
            IsShortFollowUp = seed.IsShortFollowUp
        };
    }

    private static ConversationMemorySnapshot BuildSnapshotFromSeed(RoutingWithContextSeed seed)
    {
        if (string.IsNullOrWhiteSpace(seed.ContextSummary) && seed.ActiveEntities.Count == 0)
            return new ConversationMemorySnapshot { ConversationId = seed.ConversationId ?? string.Empty };

        var summary = new ConversationSummary
        {
            Summary = seed.ContextSummary ?? string.Empty,
            ActiveEntities = seed.ActiveEntities,
            ActiveSkill = seed.ActiveSkill ?? "NONE",
            CoveredThroughTurn = seed.TurnNumber,
            KeyFacts = seed.KeyFacts ?? new()
        };

        return new ConversationMemorySnapshot
        {
            ConversationId = seed.ConversationId ?? string.Empty,
            LatestSummary = summary,
            TurnCount = seed.TurnNumber
        };
    }

    private static string BuildTeacherUserMessage(RoutingWithContextSeed seed)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(seed.ContextSummary))
        {
            sb.AppendLine("[CONVERSATION CONTEXT]:");
            sb.AppendLine(seed.ContextSummary);
            if (!string.IsNullOrWhiteSpace(seed.ActiveSkill))
                sb.Append("Active skill: ").AppendLine(seed.ActiveSkill);
            if (seed.ActiveEntities.Count > 0)
            {
                sb.Append("Active entities: ");
                sb.AppendLine(string.Join(", ",
                    seed.ActiveEntities.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }
        }
        sb.Append("[USER MESSAGE]: ").Append(seed.UserMessage);
        return sb.ToString();
    }

    private static bool ValidateFollowUpSkill(string completionJson, string expectedSkill)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(completionJson, JsonOpts);
            if (root.TryGetProperty("skills", out var skillsProp) && skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in skillsProp.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.String) continue;
                    var s = el.GetString()?.Trim() ?? string.Empty;
                    return string.Equals(s, expectedSkill, StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }

            if (!root.TryGetProperty("skill", out var skillProp) || skillProp.ValueKind != JsonValueKind.String)
                return false;

            var skill = skillProp.GetString() ?? string.Empty;
            return string.Equals(skill, expectedSkill, StringComparison.OrdinalIgnoreCase);
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

/// <summary>Seed for generating a context-aware routing example.</summary>
public sealed class RoutingWithContextSeed
{
    public string? ConversationId { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string? ContextSummary { get; init; }
    public string? ActiveSkill { get; init; }
    public Dictionary<string, string> ActiveEntities { get; init; } = new();
    public List<string>? KeyFacts { get; init; }
    public string? LastToolName { get; init; }
    public int TurnNumber { get; init; }
    public bool IsShortFollowUp { get; init; }
    public string SeedType { get; init; } = "unknown";
}

/// <summary>One context-aware routing training example (prompt + completion).</summary>
public sealed class DatasetRoutingWithContextExample
{
    public string Prompt { get; init; } = string.Empty;
    public string Completion { get; init; } = string.Empty;
    public string SeedType { get; init; } = string.Empty;
    public string UserMessage { get; init; } = string.Empty;
    public string? ActiveSkill { get; init; }
    public bool IsShortFollowUp { get; init; }
}
