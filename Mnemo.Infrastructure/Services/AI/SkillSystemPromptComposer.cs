using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class SkillSystemPromptComposer : ISkillSystemPromptComposer
{
    private const string SkillBehaviorGuardrails =
        "Skill context below is scoped to this turn only. Answer the user's question; stay concise. " +
        "If the fragment does not confirm a detail, say you are unsure. Use tools when listed instead of only describing.";

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
}
