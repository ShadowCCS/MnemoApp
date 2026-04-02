using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Builds the <c>convo_summarize</c> task prompt submitted to the manager model.
/// The format is frozen: changing it requires regenerating the training dataset
/// and retraining the manager model.
/// </summary>
internal static class ConvoSummarizePromptBuilder
{
    /// <summary>
    /// Produces the full task block for the manager model.
    /// </summary>
    /// <param name="snapshot">Current memory snapshot (facts + previous summary).</param>
    /// <param name="newTurns">Turns since the last summarization, oldest first.</param>
    public static string Build(ConversationMemorySnapshot snapshot, IReadOnlyList<ConversationTurn> newTurns)
    {
        var sb = new StringBuilder();
        sb.Append("TaskType: ").AppendLine(OrchestrationTaskTypes.ConvoSummarize);

        // Previous summary
        var prev = snapshot.LatestSummary;
        sb.Append("[PREVIOUS SUMMARY]: ");
        if (prev != null && !string.IsNullOrWhiteSpace(prev.Summary))
            sb.AppendLine(prev.Summary);
        else
            sb.AppendLine("None");

        // New turns since last summary
        sb.AppendLine("[NEW TURNS]:");
        foreach (var turn in newTurns)
        {
            var role = turn.Role == ConversationRole.User ? "User" : "Assistant";
            sb.Append(role).Append(": ").AppendLine(turn.Content.Trim());
        }

        // Working memory facts
        sb.AppendLine("[WORKING MEMORY]:");
        if (snapshot.Facts.Count > 0)
        {
            foreach (var fact in snapshot.Facts)
                sb.Append(fact.Key).Append('=').AppendLine(fact.Value);
        }
        else
        {
            sb.AppendLine("None");
        }

        AppendConvoSummarizeInstructions(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Appends the frozen <c>[INSTRUCTIONS]:</c> block used at inference and in training prompts.
    /// Keep in sync with synthetic dataset builders (Python) and <see cref="ConvoSummarizeDatasetBuilder"/>.
    /// </summary>
    internal static void AppendConvoSummarizeInstructions(StringBuilder sb)
    {
        sb.AppendLine("[INSTRUCTIONS]:");
        sb.AppendLine("Produce JSON only. Schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"<≤3 sentence prose summary written for a new assistant taking over mid-session>\",");
        sb.AppendLine("  \"active_entities\": { \"<type>\": \"<id>\" },");
        sb.AppendLine("  \"key_facts\": [\"<short phrase under 10 words>\"],");
        sb.AppendLine("  \"active_skill\": \"<Notes|Mindmap|Application|Settings|NONE>\"");
        sb.AppendLine("}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Preserve ALL entity IDs (note_id, mindmap_id, node_id, block_id) that are still relevant.");
        sb.AppendLine("- active_entities keys must be typed: note_id, mindmap_id, node_id, block_id.");
        sb.AppendLine("- Never hallucinate IDs. Only use IDs that explicitly appeared in the conversation.");
        sb.AppendLine("- active_skill: the skill the conversation was most recently operating under.");
        sb.AppendLine("- Max 250 tokens total. Output JSON only, no prose before or after.");
    }
}
