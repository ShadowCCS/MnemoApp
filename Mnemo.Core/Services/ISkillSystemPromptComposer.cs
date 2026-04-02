using System.Collections.Generic;

namespace Mnemo.Core.Services;

public interface ISkillSystemPromptComposer
{
    string Compose(string baseSystemPrompt, string? skillId);

    /// <summary>Compose using merged skill context when the user request spans multiple modules.</summary>
    string Compose(string baseSystemPrompt, IReadOnlyList<string> skillIds);
}
