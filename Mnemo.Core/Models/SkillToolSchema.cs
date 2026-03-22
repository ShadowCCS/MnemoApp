using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public sealed class SkillToolSchema
{
    [JsonPropertyName("tools")]
    public List<SkillToolDefinition> Tools { get; init; } = [];
}
