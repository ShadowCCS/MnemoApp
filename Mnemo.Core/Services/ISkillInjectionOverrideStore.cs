namespace Mnemo.Core.Services;

/// <summary>
/// Optional per-conversation skill id that replaces routed skill injection for tool lists and composer context.
/// </summary>
public interface ISkillInjectionOverrideStore
{
    void Set(string conversationKey, string? skillId);

    string? TryGet(string conversationKey);
}
