namespace Mnemo.Core.Models.TextShortcuts;

/// <summary>
/// Result of applying text shortcuts to a string.
/// </summary>
public sealed class TextShortcutResult
{
    /// <summary>
    /// Creates a new result.
    /// </summary>
    public TextShortcutResult(string text, int caretIndex)
    {
        Text = text;
        CaretIndex = caretIndex;
    }

    /// <summary>
    /// The transformed text after applying all shortcuts.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The adjusted caret index after transformations.
    /// </summary>
    public int CaretIndex { get; }

    /// <summary>
    /// Whether any transformations were applied.
    /// </summary>
    public bool WasTransformed { get; init; }

    /// <summary>
    /// Sequence matched by the final applied transformation, if any.
    /// Useful for immediate "backspace to undo auto-convert" behavior.
    /// </summary>
    public string? LastAppliedSequence { get; init; }

    /// <summary>
    /// Replacement inserted by the final applied transformation, if any.
    /// </summary>
    public string? LastAppliedReplacement { get; init; }

    /// <summary>
    /// Start index of the final applied replacement in <see cref="Text"/>.
    /// </summary>
    public int LastAppliedStartIndex { get; init; } = -1;
}
