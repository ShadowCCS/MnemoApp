using System.Text.Json.Serialization;

namespace Mnemo.Core.Models;

public sealed class SkillDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("detection_hint")]
    public string? DetectionHint { get; init; }

    [JsonPropertyName("injection")]
    public SkillInjectionOptions Injection { get; init; } = new();

    [JsonPropertyName("training_examples")]
    public List<string> TrainingExamples { get; init; } = [];
}
