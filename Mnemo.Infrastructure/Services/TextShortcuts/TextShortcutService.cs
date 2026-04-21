using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models.TextShortcuts;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.TextShortcuts;

/// <summary>
/// Default <see cref="ITextShortcutService"/> that expands ASCII sequences registered in
/// <see cref="DefaultTextShortcutCatalog"/>. Additional shortcuts can be supplied at construction
/// (e.g. from future per-user settings) without touching the editor.
/// </summary>
public sealed class TextShortcutService : ITextShortcutService
{
    private readonly IReadOnlyList<TextShortcut> _shortcuts;

    public TextShortcutService()
        : this(DefaultTextShortcutCatalog.CreateShortcuts())
    {
    }

    internal TextShortcutService(IEnumerable<TextShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        _shortcuts = shortcuts
            .OrderByDescending(s => s.Priority)
            .ThenByDescending(s => s.SequenceLength)
            .ToList();
    }

    public IReadOnlyList<TextShortcut> Shortcuts => _shortcuts;

    public TextShortcutResult Apply(string text, int caretIndex, int insertStart, int insertLength)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_shortcuts.Count == 0 || insertLength <= 0 || string.IsNullOrEmpty(text))
            return new TextShortcutResult(text, caretIndex);

        var activeStart = Math.Max(0, Math.Min(insertStart, text.Length));
        var activeEnd = Math.Max(activeStart, Math.Min(insertStart + insertLength, text.Length));

        var changed = false;
        var lastSequence = default(string);
        var lastReplacement = default(string);
        var lastStart = -1;
        // Cascading application: a replacement can create a new overlap (e.g. "<" + "→" → "↔").
        // Iterate until no shortcut overlaps the active range. A per-iteration work cap prevents
        // any pathological cycle from stalling the UI thread.
        const int maxIterations = 32;
        for (var i = 0; i < maxIterations; i++)
        {
            if (!TryApplyOne(_shortcuts, ref text, ref caretIndex, ref activeStart, ref activeEnd,
                out var appliedSequence, out var appliedReplacement, out var appliedStart))
                break;
            changed = true;
            lastSequence = appliedSequence;
            lastReplacement = appliedReplacement;
            lastStart = appliedStart;
        }

        return new TextShortcutResult(text, caretIndex)
        {
            WasTransformed = changed,
            LastAppliedSequence = lastSequence,
            LastAppliedReplacement = lastReplacement,
            LastAppliedStartIndex = lastStart
        };
    }

    private bool TryApplyOne(
        IReadOnlyList<TextShortcut> shortcuts,
        ref string text,
        ref int caretIndex,
        ref int activeStart,
        ref int activeEnd,
        out string appliedSequence,
        out string appliedReplacement,
        out int appliedStart)
    {
        foreach (var shortcut in shortcuts)
        {
            var sequence = shortcut.Sequence;
            var seqLen = sequence.Length;
            if (seqLen == 0 || seqLen > text.Length) continue;

            // Restrict search to the active window (expanded by seqLen-1 so matches straddling
            // the boundary are still found).
            var searchFrom = Math.Max(0, activeStart - (seqLen - 1));
            var searchTo = Math.Min(text.Length, activeEnd + (seqLen - 1));
            if (searchFrom >= searchTo) continue;

            var index = searchFrom;
            while (index <= searchTo - seqLen)
            {
                var found = text.IndexOf(sequence, index, searchTo - index, StringComparison.Ordinal);
                if (found < 0) break;

                var matchEnd = found + seqLen;
                var overlapsActive = matchEnd > activeStart && found < activeEnd;
                if (overlapsActive)
                {
                    ApplyReplacement(ref text, ref caretIndex, ref activeStart, ref activeEnd,
                        found, matchEnd, shortcut.Replacement);
                    appliedSequence = shortcut.Sequence;
                    appliedReplacement = shortcut.Replacement;
                    appliedStart = found;
                    return true;
                }

                index = found + 1;
            }
        }

        appliedSequence = string.Empty;
        appliedReplacement = string.Empty;
        appliedStart = -1;
        return false;
    }

    private static void ApplyReplacement(
        ref string text,
        ref int caretIndex,
        ref int activeStart,
        ref int activeEnd,
        int matchStart,
        int matchEnd,
        string replacement)
    {
        text = text.Remove(matchStart, matchEnd - matchStart).Insert(matchStart, replacement);
        var replacementEnd = matchStart + replacement.Length;

        caretIndex = ShiftPosition(caretIndex, matchStart, matchEnd, replacementEnd);
        activeStart = Math.Min(activeStart, matchStart);
        activeEnd = Math.Max(ShiftPosition(activeEnd, matchStart, matchEnd, replacementEnd), replacementEnd);
    }

    /// <summary>
    /// Shifts <paramref name="position"/> through a replacement of <c>[matchStart, matchEnd)</c> by text
    /// of length <c>replacementEnd - matchStart</c>. Positions inside the replaced span collapse to the
    /// replacement's end (matches most editors' behaviour when the caret falls inside an auto-expansion).
    /// </summary>
    private static int ShiftPosition(int position, int matchStart, int matchEnd, int replacementEnd)
    {
        if (position <= matchStart) return position;
        if (position >= matchEnd) return position + (replacementEnd - matchEnd);
        return replacementEnd;
    }
}
