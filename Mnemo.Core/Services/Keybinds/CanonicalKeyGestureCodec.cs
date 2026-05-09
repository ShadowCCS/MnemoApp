using Mnemo.Core.Models.Keybinds;

namespace Mnemo.Core.Services.Keybinds;

/// <summary>Parses and serializes canonical chord strings (logical modifiers + key token).</summary>
public static class CanonicalKeyGestureCodec
{
    public static LogicalChord ParseChord(string canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            throw new FormatException("Gesture is empty.");

        var parts = canonical.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
            throw new FormatException("Gesture has no key.");

        var keyToken = NormalizeKeyToken(parts[^1]);
        var mask = KeybindModifierMask.None;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var raw = parts[i];
            if (!seen.Add(raw))
                throw new FormatException($"Duplicate modifier '{raw}'.");

            mask |= raw.ToLowerInvariant() switch
            {
                "shift" => KeybindModifierMask.Shift,
                "alt" => KeybindModifierMask.Alt,
                "ctrl" or "control" => KeybindModifierMask.Ctrl,
                "primary" or "cmd" or "meta" => KeybindModifierMask.Primary,
                _ => throw new FormatException($"Unknown modifier '{raw}'.")
            };
        }

        return new LogicalChord(mask, keyToken);
    }

    public static string ToCanonicalString(LogicalChord chord)
    {
        var m = chord.Modifiers;
        var list = new List<string>(4);
        if (m.HasFlag(KeybindModifierMask.Alt)) list.Add("Alt");
        if (m.HasFlag(KeybindModifierMask.Ctrl)) list.Add("Ctrl");
        if (m.HasFlag(KeybindModifierMask.Primary)) list.Add("Primary");
        if (m.HasFlag(KeybindModifierMask.Shift)) list.Add("Shift");

        if (list.Count == 0)
            return chord.KeyToken;

        return string.Join('+', list) + "+" + chord.KeyToken;
    }

    public static string NormalizeChordString(string input) =>
        ToCanonicalString(ParseChord(input));

    private static string NormalizeKeyToken(string token)
    {
        token = token.Trim();
        if (token.Length == 1 && char.IsLetter(token[0]))
            return token.ToUpperInvariant();
        return token;
    }

    public static bool ChordsMatch(LogicalChord definition, KeybindPhysicalInput input)
    {
        if (!string.Equals(definition.KeyToken, input.KeyToken, StringComparison.OrdinalIgnoreCase))
            return false;
        return definition.Modifiers == input.Modifiers;
    }
}
