namespace Mnemo.Core.Models.TextShortcuts;

/// <summary>
/// Represents a text shortcut that automatically converts an ASCII sequence
/// to a Unicode character while typing.
/// </summary>
public sealed class TextShortcut
{
    /// <summary>
    /// Creates a new text shortcut.
    /// </summary>
    /// <param name="sequence">The ASCII sequence to match (e.g., "->").</param>
    /// <param name="replacement">The Unicode replacement character (e.g., "\u2192").</param>
    /// <param name="priority">Priority for evaluation order. Higher values are evaluated first.</param>
    public TextShortcut(string sequence, string replacement, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequence);
        ArgumentException.ThrowIfNullOrEmpty(replacement);

        Sequence = sequence;
        Replacement = replacement;
        Priority = priority;
    }

    /// <summary>
    /// The ASCII sequence to match (e.g., "->", "<->").
    /// </summary>
    public string Sequence { get; }

    /// <summary>
    /// The Unicode replacement string (e.g., "\u2192", "\u2194").
    /// </summary>
    public string Replacement { get; }

    /// <summary>
    /// Priority for evaluation order. Higher values are evaluated first.
    /// Used to ensure longer or more specific patterns match before shorter ones.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Length of the sequence pattern.
    /// </summary>
    public int SequenceLength => Sequence.Length;

    /// <summary>
    /// Length difference between replacement and sequence.
    /// Positive means replacement is longer.
    /// </summary>
    public int LengthDelta => Replacement.Length - Sequence.Length;
}
