using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Application;

public sealed class InjectSkillParameters
{
    /// <summary>Skill id to load (or NONE / empty to clear an override when applying).</summary>
    [JsonPropertyName("skill_id")]
    public string? SkillId { get; set; }

    /// <summary>When true and a conversation key is available, persists override for subsequent user turns.</summary>
    [JsonPropertyName("apply_for_conversation")]
    public bool ApplyForConversation { get; set; } = true;
}
