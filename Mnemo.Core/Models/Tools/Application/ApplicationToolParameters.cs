using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Application;

public sealed class NavigateToParameters
{
    [JsonPropertyName("destination")] public string Destination { get; set; } = string.Empty;
    [JsonPropertyName("entity_id")] public string? EntityId { get; set; }
}

public sealed class OpenSettingsParameters
{
    [JsonPropertyName("section")] public string? Section { get; set; }
}
