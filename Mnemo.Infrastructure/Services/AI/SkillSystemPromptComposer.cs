using System.Collections.Generic;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class SkillSystemPromptComposer : ISkillSystemPromptComposer
{
    private const string SkillBehaviorGuardrails =
        "You have tools available. When the user asks you to do something (create, edit, read, navigate, change settings), " +
        "call the appropriate tool — do NOT describe what you would do, just call it. After the tool returns, summarize the " +
        "result to the user in 1–2 sentences. Skill context below is scoped to this turn only; stay concise. " +
        "If the fragment does not confirm a detail, say you are unsure. " +
        "Skill discovery: get_skills only lists skill ids. To read or change Notes/Mindmap/Settings data you MUST call inject_skill " +
        "with the matching skill_id (e.g. Notes). fetch_skill is preview-only and does not enable list_notes etc. " +
        "navigate_to only switches UI; it does not load module tools — never use it instead of inject_skill.";

    private readonly ISkillRegistry _skillRegistry;

    public SkillSystemPromptComposer(ISkillRegistry skillRegistry)
    {
        _skillRegistry = skillRegistry;
    }

    public string Compose(string baseSystemPrompt, string? skillId)
    {
        var injection = _skillRegistry.GetInjection(skillId);
        if (string.IsNullOrWhiteSpace(injection.SystemPromptFragment))
            return baseSystemPrompt;

        return $"{baseSystemPrompt}\n\n{SkillBehaviorGuardrails}\n{injection.SystemPromptFragment}";
    }

    public string Compose(string baseSystemPrompt, IReadOnlyList<string> skillIds)
    {
        var injection = _skillRegistry.GetMergedInjection(skillIds);
        if (string.IsNullOrWhiteSpace(injection.SystemPromptFragment))
            return baseSystemPrompt;

        return $"{baseSystemPrompt}\n\n{SkillBehaviorGuardrails}\n{injection.SystemPromptFragment}";
    }
}
