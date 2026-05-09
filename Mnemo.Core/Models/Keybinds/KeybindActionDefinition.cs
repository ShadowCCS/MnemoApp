namespace Mnemo.Core.Models.Keybinds;

/// <summary>Registered action with one or more chord/sequence alternatives.</summary>
public sealed class KeybindActionDefinition
{
    public required string ActionId { get; init; }
    public required string Namespace { get; init; }
    public KeybindScope Scope { get; init; }
    /// <summary>When true, overlay may show that this binding suppresses globals in its UI context.</summary>
    public bool SuppressesGlobalsInContext { get; init; }
    public IReadOnlyList<KeybindBindingEntry> Bindings { get; init; } = Array.Empty<KeybindBindingEntry>();
    public IReadOnlyList<string> ObsoleteIds { get; init; } = Array.Empty<string>();
    /// <summary>When false, action has no effective bindings after merge.</summary>
    public bool Enabled { get; init; } = true;
}
