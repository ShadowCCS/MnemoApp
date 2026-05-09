namespace Mnemo.Core.Models.Keybinds;

/// <summary>Platform-neutral key down after UI normalization (Meta → Primary where applicable).</summary>
public readonly struct KeybindPhysicalInput : IEquatable<KeybindPhysicalInput>
{
    public KeybindPhysicalInput(KeybindModifierMask modifiers, string keyToken)
    {
        Modifiers = modifiers;
        KeyToken = keyToken ?? throw new ArgumentNullException(nameof(keyToken));
    }

    public KeybindModifierMask Modifiers { get; }
    public string KeyToken { get; }

    public bool Equals(KeybindPhysicalInput other) =>
        Modifiers == other.Modifiers &&
        string.Equals(KeyToken, other.KeyToken, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is KeybindPhysicalInput other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)Modifiers, KeyToken);

    public static bool operator ==(KeybindPhysicalInput left, KeybindPhysicalInput right) => left.Equals(right);

    public static bool operator !=(KeybindPhysicalInput left, KeybindPhysicalInput right) => !left.Equals(right);
}
