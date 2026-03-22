namespace Mnemo.Core.Models;

public sealed class SkillInjectionContext
{
    public string? SystemPromptFragment { get; init; }
    public IReadOnlyList<SkillToolDefinition> Tools { get; init; } = [];
}
