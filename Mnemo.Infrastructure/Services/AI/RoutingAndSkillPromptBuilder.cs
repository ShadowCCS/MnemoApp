using System.Linq;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

internal static class RoutingAndSkillPromptBuilder
{
    public static string Build(string userMessage, IReadOnlyList<SkillDefinition> enabledSkills)
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
            "- NONE: General chat, study help, or topics that do not match any module skill above. Prefer a module skill (e.g. Notes) when the user names that module or its UI (sidebar, favorites, folders, editor).");

        sb.Append("[USER MESSAGE]: ").Append(userMessage);
        return sb.ToString();
    }
}
