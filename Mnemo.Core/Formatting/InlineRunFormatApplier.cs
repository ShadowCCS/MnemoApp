using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.Core.Formatting;

/// <summary>
/// Pure-logic applier for inline formatting on a run list (Notion-style).
/// All operations produce a normalized list (no adjacent runs with equal styles).
/// </summary>
public static class InlineRunFormatApplier
{
    /// <summary>
    /// Toggle a format kind over the selection range [start, end) in the flattened text.
    /// Returns a new normalized run list.
    /// </summary>
    public static List<InlineRun> Apply(
        IReadOnlyList<InlineRun> runs, int start, int end, InlineFormatKind kind, string? color = null)
    {
        if (runs.Count == 0 || start < 0 || end <= start)
            return new List<InlineRun>(runs);

        if (kind == InlineFormatKind.Subscript || kind == InlineFormatKind.Superscript)
            return new List<InlineRun>(runs);

        var split = SplitAtBoundaries(runs, start, end);

        bool allHaveFormat = AllRunsInRangeHaveFormat(split, start, end, kind, color);

        var result = new List<InlineRun>(split.Count);
        int offset = 0;
        foreach (var run in split)
        {
            int runEnd = offset + run.Text.Length;
            bool isInRange = offset < end && runEnd > start;

            if (isInRange)
            {
                InlineStyle newStyle;
                if (kind == InlineFormatKind.BackgroundColor)
                    newStyle = allHaveFormat
                        ? run.Style.WithClear(kind)
                        : run.Style.WithSet(kind, color);
                else if (kind == InlineFormatKind.Highlight)
                    newStyle = allHaveFormat
                        ? run.Style.WithClear(kind)
                        : run.Style.WithSet(kind, color);
                else
                    newStyle = allHaveFormat
                        ? run.Style.WithClear(kind)
                        : run.Style.WithSet(kind);

                result.Add(new InlineRun(run.Text, newStyle));
            }
            else
            {
                result.Add(run);
            }

            offset = runEnd;
        }

        return Normalize(result);
    }

    /// <summary>
    /// Merge adjacent runs with identical styles. Always call after any mutation.
    /// </summary>
    public static List<InlineRun> Normalize(IReadOnlyList<InlineRun> runs)
    {
        if (runs.Count == 0)
            return new List<InlineRun>();

        var result = new List<InlineRun>(runs.Count);
        var current = runs[0];

        for (int i = 1; i < runs.Count; i++)
        {
            if (runs[i].Style == current.Style)
            {
                current = current with { Text = current.Text + runs[i].Text };
            }
            else
            {
                if (current.Text.Length > 0)
                    result.Add(current);
                current = runs[i];
            }
        }

        if (current.Text.Length > 0)
            result.Add(current);

        return result;
    }

    /// <summary>
    /// Flatten a run list to a plain string.
    /// </summary>
    public static string Flatten(IReadOnlyList<InlineRun> runs)
    {
        if (runs.Count == 0) return string.Empty;
        if (runs.Count == 1) return runs[0].Text;
        return string.Concat(runs.Select(r => r.Text));
    }

    /// <summary>
    /// Apply a text edit (replace old flattened text with new) while preserving run styles.
    /// The edit region is determined by diffing oldText vs newText.
    /// Returns a new normalized run list.
    /// </summary>
    public static List<InlineRun> ApplyTextEdit(IReadOnlyList<InlineRun> runs, string oldText, string newText)
    {
        if (runs.Count == 0)
            return new List<InlineRun> { InlineRun.Plain(newText) };

        if (oldText == newText)
            return new List<InlineRun>(runs);

        int commonPrefix = 0;
        int minLen = System.Math.Min(oldText.Length, newText.Length);
        while (commonPrefix < minLen && oldText[commonPrefix] == newText[commonPrefix])
            commonPrefix++;

        int commonSuffix = 0;
        while (commonSuffix < minLen - commonPrefix
               && oldText[oldText.Length - 1 - commonSuffix] == newText[newText.Length - 1 - commonSuffix])
            commonSuffix++;

        int deleteStart = commonPrefix;
        int deleteEnd = oldText.Length - commonSuffix;
        string inserted = newText.Substring(commonPrefix, newText.Length - commonPrefix - commonSuffix);

        var result = new List<InlineRun>();
        int offset = 0;

        InlineStyle insertStyle = InlineStyle.Default;
        bool foundInsertStyle = false;

        foreach (var run in runs)
        {
            int runEnd = offset + run.Text.Length;

            if (runEnd <= deleteStart || offset >= deleteEnd)
            {
                result.Add(run);
            }
            else
            {
                if (offset < deleteStart)
                {
                    result.Add(new InlineRun(run.Text[..(deleteStart - offset)], run.Style));
                }

                if (!foundInsertStyle)
                {
                    insertStyle = run.Style;
                    foundInsertStyle = true;
                    if (inserted.Length > 0)
                        result.Add(new InlineRun(inserted, run.Style));
                }

                if (runEnd > deleteEnd)
                {
                    result.Add(new InlineRun(run.Text[(deleteEnd - offset)..], run.Style));
                }
            }

            offset = runEnd;
        }

        if (!foundInsertStyle && inserted.Length > 0)
            result.Add(new InlineRun(inserted, InlineStyle.Default));

        if (result.Count == 0)
            result.Add(InlineRun.Plain(string.Empty));

        return Normalize(result);
    }

    private static List<InlineRun> SplitAtBoundaries(IReadOnlyList<InlineRun> runs, int start, int end)
    {
        var result = new List<InlineRun>();
        int offset = 0;

        foreach (var run in runs)
        {
            int runEnd = offset + run.Text.Length;

            int splitStart = start - offset;
            int splitEnd = end - offset;

            if (splitStart > 0 && splitStart < run.Text.Length && splitEnd > 0)
            {
                result.Add(new InlineRun(run.Text[..splitStart], run.Style));

                if (splitEnd > splitStart && splitEnd < run.Text.Length)
                {
                    result.Add(new InlineRun(run.Text[splitStart..splitEnd], run.Style));
                    result.Add(new InlineRun(run.Text[splitEnd..], run.Style));
                }
                else
                {
                    result.Add(new InlineRun(run.Text[splitStart..], run.Style));
                }
            }
            else if (splitEnd > 0 && splitEnd < run.Text.Length && (splitStart <= 0))
            {
                result.Add(new InlineRun(run.Text[..splitEnd], run.Style));
                result.Add(new InlineRun(run.Text[splitEnd..], run.Style));
            }
            else
            {
                result.Add(run);
            }

            offset = runEnd;
        }

        return result;
    }

    private static bool AllRunsInRangeHaveFormat(
        IReadOnlyList<InlineRun> runs, int start, int end, InlineFormatKind kind, string? color)
    {
        int offset = 0;
        foreach (var run in runs)
        {
            int runEnd = offset + run.Text.Length;
            if (offset < end && runEnd > start)
            {
                if (kind == InlineFormatKind.BackgroundColor)
                {
                    if (run.Style.BackgroundColor != color)
                        return false;
                }
                else if (!run.Style.Has(kind))
                {
                    return false;
                }
            }

            offset = runEnd;
        }

        return true;
    }
}
