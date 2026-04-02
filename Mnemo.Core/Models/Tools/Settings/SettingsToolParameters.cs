using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Settings;

public sealed class GetSettingParameters
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
}

public sealed class SetSettingParameters
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value")] public object? Value { get; set; }
}

public sealed class ListSettingsParameters
{
    [JsonPropertyName("category")] public string? Category { get; set; }
}

public sealed class ResetSettingParameters
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
}
