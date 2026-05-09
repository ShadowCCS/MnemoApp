namespace Mnemo.Core.Models.Keybinds;

/// <summary>What global keybind matching should treat as suppressed for the current scope.</summary>
public sealed class KeybindSuppressionPolicy
{
    public bool SuppressAll { get; init; }
    public IReadOnlyList<string>? OnlyActionIds { get; init; }
    public IReadOnlyList<string>? OnlyNamespaces { get; init; }
}
