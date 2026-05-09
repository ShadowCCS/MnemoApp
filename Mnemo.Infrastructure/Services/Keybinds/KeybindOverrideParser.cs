using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services.Keybinds;

namespace Mnemo.Infrastructure.Services.Keybinds;

internal static class KeybindOverrideParser
{
    public static IReadOnlyList<KeybindBindingEntry> ParseBindings(KeybindOverrideDocument doc)
    {
        if (doc.Bindings == null || doc.Bindings.Count == 0)
            return Array.Empty<KeybindBindingEntry>();

        var list = new List<KeybindBindingEntry>();
        foreach (var dto in doc.Bindings)
        {
            var kind = (dto.Kind ?? "chord").ToLowerInvariant();
            if (kind == "chord")
            {
                if (string.IsNullOrWhiteSpace(dto.Gesture))
                    continue;
                var chord = CanonicalKeyGestureCodec.ParseChord(dto.Gesture);
                list.Add(new KeybindBindingEntry { Kind = KeybindBindingKind.Chord, Chord = chord });
            }
            else if (kind == "sequence")
            {
                if (dto.Steps == null || dto.Steps.Count == 0)
                    continue;
                var steps = new List<LogicalChord>();
                foreach (var step in dto.Steps)
                {
                    if (string.IsNullOrWhiteSpace(step)) continue;
                    steps.Add(CanonicalKeyGestureCodec.ParseChord(step));
                }

                if (steps.Count > 0)
                    list.Add(new KeybindBindingEntry { Kind = KeybindBindingKind.Sequence, SequenceSteps = steps });
            }
        }

        return list;
    }
}
