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

    /// <summary>
    /// When false, the skill is omitted from <c>get_skills</c> (small-model-friendly catalog).
    /// Hidden skills remain injectable by id (often discovered via the Core tool <c>get_analytics_skills</c>).
    /// Absent or null counts as true (shown in <c>get_skills</c>).
    /// </summary>
    [JsonPropertyName("expose_in_get_skills")]
    public bool? ExposeInGetSkills { get; init; }

    [JsonPropertyName("injection")]
    public SkillInjectionOptions Injection { get; init; } = new();

    [JsonPropertyName("training_examples")]
    public List<string> TrainingExamples { get; init; } = [];
}
