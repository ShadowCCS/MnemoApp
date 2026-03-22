using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class SkillSystemPromptComposer : ISkillSystemPromptComposer
{
    private const string SkillBehaviorGuardrails = @"When a skill is injected, treat it as scoped guidance, not a checklist to dump.

Skill response rules:
- Answer only what the user asked. Do not add unrelated settings, features, or side notes.
- Keep routine UI answers concise (usually 1-3 short bullets or 1 short paragraph).
- Prefer direct, concrete steps over broad app descriptions.
- If skill facts do not confirm a detail, say you are not sure instead of guessing.
- Avoid repeating the same fact in different wording.";

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
