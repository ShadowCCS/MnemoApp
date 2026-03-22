using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public sealed class SkillToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}
