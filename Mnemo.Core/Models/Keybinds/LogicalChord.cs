namespace Mnemo.Core.Models.Keybinds;

/// <summary>One chord step: logical modifiers + key token (e.g. <c>K</c>, <c>G</c>, <c>OemQuestion</c>).</summary>
public readonly struct LogicalChord : IEquatable<LogicalChord>
{
    public LogicalChord(KeybindModifierMask modifiers, string keyToken)
    {
        Modifiers = modifiers;
        KeyToken = keyToken ?? throw new ArgumentNullException(nameof(keyToken));
    }

    public KeybindModifierMask Modifiers { get; }
    public string KeyToken { get; }

    public bool Equals(LogicalChord other) =>
        Modifiers == other.Modifiers &&
        string.Equals(KeyToken, other.KeyToken, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is LogicalChord other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)Modifiers, KeyToken);

    public static bool operator ==(LogicalChord left, LogicalChord right) => left.Equals(right);

    public static bool operator !=(LogicalChord left, LogicalChord right) => !left.Equals(right);
}
