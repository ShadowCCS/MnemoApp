using System.Linq;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

internal static class RoutingAndSkillPromptBuilder
{
    /// <summary>
    /// Builds the routing task block for the manager model.
    /// When a memory snapshot with a summary is available, a rich <c>[CONVERSATION CONTEXT]</c>
    /// block is emitted instead of the single-slot <c>[RECENT TOOL CONTEXT]</c> hint.
    /// This allows the model to correctly classify short follow-ups ("yes", "do it", "add more")
    /// using the full context of what was previously discussed.
    /// </summary>
    public static string Build(
        string userMessage,
        IReadOnlyList<SkillDefinition> enabledSkills,
        RoutingToolHint? recentToolHint = null,
        ConversationMemorySnapshot? memorySnapshot = null)
    {
        var sb = new StringBuilder();
        sb.Append("TaskType: ").Append(OrchestrationTaskTypes.RoutingAndSkillDetection).Append('\n');

        var skillNames = enabledSkills
            .Select(s => s.Id)
            .Concat(["NONE"]);
        sb.Append("[AVAILABLE SKILLS]: ").Append(string.Join(", ", skillNames)).Append('\n');

        sb.AppendLine("[SKILL DESCRIPTIONS]:");
        foreach (var skill in enabledSkills)
        {
            sb.Append("- ").Append(skill.Id).Append(": ").Append(skill.Description);
            if (!string.IsNullOrWhiteSpace(skill.DetectionHint))
                sb.Append(" [signals: ").Append(skill.DetectionHint.Trim()).Append(']');
            sb.Append('\n');
        }

        sb.AppendLine(
            "- NONE: General study or subject Q&A when the user is not asking about the Mnemo app or a specific module. " +
            "If they ask about Mnemo overall, navigation, themes, or app-wide \"where do I…\", choose Application—not NONE. " +
            "If they ask to change a specific setting (language, editor toggles, AI toggles, profile name) using tool workflows, choose Settings—not NONE. " +
            "If they clearly mean Notes, Mindmap, or Learning Path content/workflow, choose that skill—not NONE. " +
            "If they ask for in-app statistics/aggregates/telemetry (counts, streaks, totals stored by Mnemo), choose Analytics—not NONE.");
        sb.AppendLine(
            "Output JSON field \"skills\": a non-empty array of skill ids. One module → one element (e.g. [\"Notes\"]). " +
            "Several modules in one request → distinct ids in order of work (e.g. [\"Notes\",\"Mindmap\"]). General chat → [\"NONE\"].");

        // Prefer rich memory context over the single-slot routing hint
        if (memorySnapshot?.LatestSummary != null)
        {
            var summary = memorySnapshot.LatestSummary;
            sb.AppendLine("[CONVERSATION CONTEXT]:");
            sb.Append("Summary: ").AppendLine(summary.Summary.Trim());

            if (!string.IsNullOrWhiteSpace(summary.ActiveSkill) && summary.ActiveSkill != "NONE")
                sb.Append("Active skill: ").AppendLine(summary.ActiveSkill);

            if (summary.ActiveEntities.Count > 0)
            {
                sb.Append("Active entities: ");
                sb.AppendLine(string.Join(", ",
                    summary.ActiveEntities.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            // Still include the most recent tool hint as a last-action signal
            if (recentToolHint != null)
                sb.Append("Last tool: ").AppendLine(recentToolHint.ToolName);

            sb.AppendLine(
                "If the user's message is a short follow-up (e.g. \"yes\", \"do it\", \"add more\", \"continue\") " +
                "that clearly continues the active skill and entities above, choose that skill—not NONE.");
        }
        else if (recentToolHint != null)
        {
            // Fallback: single-slot hint for early turns before any summary exists
            sb.Append("[RECENT TOOL CONTEXT]: In this thread the assistant last ran tool \"")
                .Append(recentToolHint.ToolName)
                .Append("\" under skill \"")
                .Append(recentToolHint.SkillId)
                .Append("\".");
            if (!string.IsNullOrWhiteSpace(recentToolHint.Detail))
            {
                sb.Append(" Result summary: ").Append(recentToolHint.Detail.Trim());
            }

            sb.AppendLine();
            sb.AppendLine(
                "If the user's message is a short follow-up (e.g. add more, continue, same note) that clearly continues that action, choose the skill above—not NONE.");
        }

        sb.Append("[USER MESSAGE]: ").Append(userMessage);
        return sb.ToString();
    }
}
