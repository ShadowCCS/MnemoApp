using System.Collections.Generic;
using Mnemo.Core.Models.TextShortcuts;

namespace Mnemo.Core.Services;

/// <summary>
/// Resolves ASCII-to-Unicode text shortcuts (e.g. <c>-&gt;</c> → <c>→</c>) while the user types.
/// Implementations must be thread-safe; consumers typically call <see cref="Apply"/> on every insertion.
/// </summary>
public interface ITextShortcutService
{
    /// <summary>
    /// Registered shortcuts in evaluation order (longest / highest priority first).
    /// </summary>
    IReadOnlyList<TextShortcut> Shortcuts { get; }

    /// <summary>
    /// Applies any shortcut whose match region overlaps the range <c>[insertStart, insertStart + insertLength)</c>.
    /// Only overlapping matches are considered so pre-existing occurrences elsewhere in the text are preserved.
    /// Replacements cascade: if a replacement itself creates a new overlap (e.g. typing <c>&lt;</c> before an
    /// existing <c>→</c> produces <c>&lt;→</c> and then <c>↔</c>), additional shortcuts are applied in turn.
    /// </summary>
    /// <param name="text">Current (post-insertion) text.</param>
    /// <param name="caretIndex">Caret index within <paramref name="text"/>.</param>
    /// <param name="insertStart">Start of the just-inserted range in <paramref name="text"/>.</param>
    /// <param name="insertLength">Length of the just-inserted range.</param>
    TextShortcutResult Apply(string text, int caretIndex, int insertStart, int insertLength);
}
