namespace Mnemo.Core.Models.Keybinds;

/// <summary>One alternative binding: either a chord or an ordered sequence of chords.</summary>
public sealed class KeybindBindingEntry
{
    public KeybindBindingKind Kind { get; init; }
    public LogicalChord? Chord { get; init; }
    public IReadOnlyList<LogicalChord>? SequenceSteps { get; init; }
}
