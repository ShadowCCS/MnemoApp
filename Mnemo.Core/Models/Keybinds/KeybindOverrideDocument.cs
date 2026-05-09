using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Keybinds;

/// <summary>JSON shape for SQLite override row (System.Text.Json).</summary>
public sealed class KeybindOverrideDocument
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("bindings")]
    public List<KeybindOverrideBindingDto>? Bindings { get; set; }
}

public sealed class KeybindOverrideBindingDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "chord";

    /// <summary>Canonical chord string e.g. <c>Primary+K</c>.</summary>
    [JsonPropertyName("gesture")]
    public string? Gesture { get; set; }

    [JsonPropertyName("steps")]
    public List<string>? Steps { get; set; }
}
