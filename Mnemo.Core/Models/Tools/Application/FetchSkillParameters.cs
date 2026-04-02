using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Application;

public sealed class FetchSkillParameters
{
    [JsonPropertyName("skill_id")]
    public string SkillId { get; set; } = string.Empty;
}
