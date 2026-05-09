using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mnemo.Core.Models.Keybinds;

namespace Mnemo.Core.Services.Keybinds;

/// <summary>Human- and platform-friendly chord labels (storage still uses <see cref="CanonicalKeyGestureCodec"/>).</summary>
public static class KeybindGestureDisplayFormatter
{
    public static string FormatChord(LogicalChord chord)
    {
        var mac = OperatingSystem.IsMacOS();
        var m = chord.Modifiers;
        if (mac)
        {
            var sb = new StringBuilder();
            if (m.HasFlag(KeybindModifierMask.Ctrl))
                sb.Append('⌃');
            if (m.HasFlag(KeybindModifierMask.Alt))
                sb.Append('⌥');
            if (m.HasFlag(KeybindModifierMask.Shift))
                sb.Append('⇧');
            if (m.HasFlag(KeybindModifierMask.Primary))
                sb.Append('⌘');
            sb.Append(FormatKeyToken(chord.KeyToken, mac));
            return sb.ToString();
        }

        var parts = new List<string>(5);
        if (m.HasFlag(KeybindModifierMask.Ctrl))
            parts.Add("Ctrl");
        if (m.HasFlag(KeybindModifierMask.Alt))
            parts.Add("Alt");
        if (m.HasFlag(KeybindModifierMask.Shift))
            parts.Add("Shift");
        if (m.HasFlag(KeybindModifierMask.Primary))
            parts.Add("Ctrl");
        parts.Add(FormatKeyToken(chord.KeyToken, mac));
        return string.Join('+', parts);
    }

    private static string FormatKeyToken(string token, bool mac)
    {
        if (string.IsNullOrEmpty(token))
            return "?";

        if (token.Length == 1 && char.IsLetter(token[0]))
            return token.ToUpperInvariant();

        return token switch
        {
            "OemComma" => ",",
            "OemPeriod" => ".",
            "Back" => mac ? "⌫" : "Backspace",
            "Delete" => "Del",
            "Escape" => mac ? "⎋" : "Esc",
            "Return" or "Enter" => mac ? "↩" : "Enter",
            "Tab" => mac ? "⇥" : "Tab",
            "Space" => "Space",
            "D0" or "NumPad0" => "0",
            "D1" or "NumPad1" => "1",
            "D2" or "NumPad2" => "2",
            "D3" or "NumPad3" => "3",
            "D4" or "NumPad4" => "4",
            "D5" or "NumPad5" => "5",
            "D6" or "NumPad6" => "6",
            "D7" or "NumPad7" => "7",
            "D8" or "NumPad8" => "8",
            "D9" or "NumPad9" => "9",
            _ => token
        };
    }

    public static string FormatBindingEntry(KeybindBindingEntry b)
    {
        if (b.Kind == KeybindBindingKind.Chord && b.Chord is { } ch)
            return FormatChord(ch);
        if (b.Kind == KeybindBindingKind.Sequence && b.SequenceSteps is { Count: > 0 } steps)
            return string.Join(" → ", steps.Select(FormatChord));
        return "?";
    }

    /// <summary>Modifier keys and key token as separate labels for kbd-style pills (sequences include <see cref="KeybindDisplayPill.IsThenSeparator"/> steps).</summary>
    public static IReadOnlyList<KeybindDisplayPill> FormatBindingDisplayPills(KeybindBindingEntry b)
    {
        if (b.Kind == KeybindBindingKind.Chord && b.Chord is { } ch)
            return FormatChordDisplayPills(ch);
        if (b.Kind == KeybindBindingKind.Sequence && b.SequenceSteps is { Count: > 0 } steps)
        {
            var list = new List<KeybindDisplayPill>();
            for (var i = 0; i < steps.Count; i++)
            {
                if (i > 0)
                    list.Add(new KeybindDisplayPill(true, "then"));
                list.AddRange(FormatChordDisplayPills(steps[i]));
            }
            return list;
        }
        return [new KeybindDisplayPill(false, "?")];
    }

    private static IReadOnlyList<KeybindDisplayPill> FormatChordDisplayPills(LogicalChord chord)
    {
        var mac = OperatingSystem.IsMacOS();
        var m = chord.Modifiers;
        var parts = new List<KeybindDisplayPill>(5);
        if (mac)
        {
            if (m.HasFlag(KeybindModifierMask.Ctrl))
                parts.Add(new KeybindDisplayPill(false, "⌃"));
            if (m.HasFlag(KeybindModifierMask.Alt))
                parts.Add(new KeybindDisplayPill(false, "⌥"));
            if (m.HasFlag(KeybindModifierMask.Shift))
                parts.Add(new KeybindDisplayPill(false, "⇧"));
            if (m.HasFlag(KeybindModifierMask.Primary))
                parts.Add(new KeybindDisplayPill(false, "⌘"));
            parts.Add(new KeybindDisplayPill(false, FormatKeyToken(chord.KeyToken, mac)));
            return parts;
        }

        if (m.HasFlag(KeybindModifierMask.Ctrl))
            parts.Add(new KeybindDisplayPill(false, "Ctrl"));
        if (m.HasFlag(KeybindModifierMask.Alt))
            parts.Add(new KeybindDisplayPill(false, "Alt"));
        if (m.HasFlag(KeybindModifierMask.Shift))
            parts.Add(new KeybindDisplayPill(false, "Shift"));
        if (m.HasFlag(KeybindModifierMask.Primary))
            parts.Add(new KeybindDisplayPill(false, "Ctrl"));
        parts.Add(new KeybindDisplayPill(false, FormatKeyToken(chord.KeyToken, mac)));
        return parts;
    }
}
