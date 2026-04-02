using System.Linq;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

internal static class RoutingAndSkillPromptBuilder
{
    public static string Build(string userMessage, IReadOnlyList<SkillDefinition> enabledSkills, RoutingToolHint? recentToolHint = null)
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
            "If they clearly mean Notes, Mindmap, or Learning Path content/workflow, choose that skill—not NONE.");

        if (recentToolHint != null)
        {
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
