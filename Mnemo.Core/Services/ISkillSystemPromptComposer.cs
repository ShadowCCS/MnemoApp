namespace Mnemo.Core.Services;

public interface ISkillSystemPromptComposer
{
    string Compose(string baseSystemPrompt, string? skillId);
}
