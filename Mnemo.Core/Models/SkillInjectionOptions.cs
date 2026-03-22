using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public sealed class SkillInjectionOptions
{
    [JsonPropertyName("system_prompt_fragment")]
    public string? SystemPromptFragment { get; init; }

    [JsonPropertyName("include_tools")]
    public bool IncludeTools { get; init; }
}
