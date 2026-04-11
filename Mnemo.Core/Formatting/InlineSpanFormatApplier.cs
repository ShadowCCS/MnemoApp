using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.Core.Formatting;

/// <summary>Formatting and text edits on <see cref="InlineSpan"/> lists.</summary>
public static class InlineSpanFormatApplier
{
    private static int SpanLen(InlineSpan s) =>
        s is TextSpan t ? t.Text.Length : 1;

    public static List<InlineSpan> Apply(
        IReadOnlyList<InlineSpan> spans, int start, int end, InlineFormatKind kind, string? color = null)
    {
        if (spans.Count == 0 || start < 0 || end <= start)
            return new List<InlineSpan>(spans);

        if (kind == InlineFormatKind.Subscript || kind == InlineFormatKind.Superscript)
            return new List<InlineSpan>(spans);

        if (kind == InlineFormatKind.Equation)
            return ApplyEquation(spans, start, end, color);

        var split = SplitAtBoundaries(spans, start, end);
        bool allHaveFormat = AllSpansInRangeHaveFormat(split, start, end, kind, color);
        var result = new List<InlineSpan>(split.Count);
        int offset = 0;
        foreach (var span in split)
        {
            int runEnd = offset + SpanLen(span);
            bool isInRange = offset < end && runEnd > start;

            if (isInRange)
            {
                TextStyle newStyle;
                if (kind == InlineFormatKind.Link)
                {
                    newStyle = color == null
                        ? GetStyle(span).WithClear(InlineFormatKind.Link)
                        : GetStyle(span).WithSet(InlineFormatKind.Link, color);
                }
                else if (kind == InlineFormatKind.BackgroundColor)
                    newStyle = allHaveFormat
                        ? GetStyle(span).WithClear(kind)
                        : GetStyle(span).WithSet(kind, color);
                else if (kind == InlineFormatKind.Highlight)
                    newStyle = allHaveFormat
                        ? GetStyle(span).WithClear(kind)
                        : GetStyle(span).WithSet(kind, color);
                else
                    newStyle = allHaveFormat
                        ? GetStyle(span).WithClear(kind)
                        : GetStyle(span).WithSet(kind);

                result.Add(SetStyle(span, newStyle));
            }
            else
                result.Add(span);

            offset = runEnd;
        }

        return Normalize(result);
    }

    private static TextStyle GetStyle(InlineSpan s) =>
        s is TextSpan t ? t.Style : ((EquationSpan)s).Style;

    private static InlineSpan SetStyle(InlineSpan s, TextStyle style) =>
        s is TextSpan t ? t with { Style = style } : ((EquationSpan)s) with { Style = style };

    public static List<InlineSpan> Normalize(IReadOnlyList<InlineSpan> spans)
    {
        if (spans.Count == 0)
            return new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        var result = new List<InlineSpan>(spans.Count);
        var current = spans[0];

        for (int i = 1; i < spans.Count; i++)
        {
            var next = spans[i];
            if (current is TextSpan ct && next is TextSpan nt && ct.Style == nt.Style)
            {
                current = ct with { Text = ct.Text + nt.Text };
            }
            else
            {
                if (current is TextSpan tx)
                {
                    if (tx.Text.Length > 0)
                        result.Add(current);
                }
                else
                    result.Add(current);

                current = next;
            }
        }

        if (current is TextSpan tcx)
        {
            if (tcx.Text.Length > 0)
                result.Add(current);
        }
        else
            result.Add(current);

        if (result.Count == 0)
            result.Add(InlineSpan.Plain(string.Empty));

        return result;
    }

    public static List<InlineSpan> SliceRuns(IReadOnlyList<InlineSpan> spans, int start, int end)
    {
        if (spans.Count == 0 || start >= end)
            return new List<InlineSpan>();

        int totalLen = InlineSpanText.LogicalLength(spans);
        start = Math.Clamp(start, 0, totalLen);
        end = Math.Clamp(end, 0, totalLen);
        if (start >= end)
            return new List<InlineSpan>();

        var result = new List<InlineSpan>();
        int offset = 0;
        foreach (var span in spans)
        {
            int runEnd = offset + SpanLen(span);
            int segStart = Math.Max(start, offset);
            int segEnd = Math.Min(end, runEnd);
            if (segStart < segEnd)
            {
                if (span is TextSpan t)
                {
                    int localA = segStart - offset;
                    int localB = segEnd - offset;
                    result.Add(new TextSpan(t.Text[localA..localB], t.Style));
                }
                else if (span is EquationSpan e && segStart == offset && segEnd == runEnd)
                    result.Add(e);
            }

            offset = runEnd;
        }

        return Normalize(result);
    }

    public static List<InlineSpan> ReplaceRange(
        IReadOnlyList<InlineSpan> spans, int start, int end, IReadOnlyList<InlineSpan> insertion)
    {
        int len = InlineSpanText.LogicalLength(spans);
        start = Math.Clamp(start, 0, len);
        end = Math.Clamp(end, 0, len);
        if (start > end)
            (start, end) = (end, start);

        var head = SliceRuns(spans, 0, start);
        var tail = SliceRuns(spans, end, len);
        var combined = new List<InlineSpan>(head.Count + insertion.Count + tail.Count);
        combined.AddRange(head);
        combined.AddRange(insertion);
        combined.AddRange(tail);
        var merged = Normalize(combined);
        if (merged.Count == 0)
            merged.Add(InlineSpan.Plain(string.Empty));
        return merged;
    }

    public static string Flatten(IReadOnlyList<InlineSpan> spans) => InlineSpanText.FlattenEditing(spans);

    public static List<InlineSpan> ApplyTextEdit(IReadOnlyList<InlineSpan> spans, string oldText, string newText)
    {
        if (spans.Count == 0)
            return new List<InlineSpan> { InlineSpan.Plain(newText) };

        if (oldText == newText)
            return new List<InlineSpan>(spans);

        int commonPrefix = 0;
        int minLen = Math.Min(oldText.Length, newText.Length);
        while (commonPrefix < minLen && oldText[commonPrefix] == newText[commonPrefix])
            commonPrefix++;

        int commonSuffix = 0;
        while (commonSuffix < minLen - commonPrefix
               && oldText[oldText.Length - 1 - commonSuffix] == newText[newText.Length - 1 - commonSuffix])
            commonSuffix++;

        int deleteStart = commonPrefix;
        int deleteEnd = oldText.Length - commonSuffix;
        string inserted = newText.Substring(commonPrefix, newText.Length - commonPrefix - commonSuffix);

        var result = new List<InlineSpan>();
        int offset = 0;

        TextStyle insertStyle = TextStyle.Default;
        bool foundInsertStyle = false;

        foreach (var span in spans)
        {
            int runEnd = offset + SpanLen(span);

            if (runEnd <= deleteStart || offset >= deleteEnd)
            {
                if (deleteStart == deleteEnd && inserted.Length > 0 && !foundInsertStyle)
                {
                    if (runEnd == deleteStart)
                    {
                        result.Add(span);
                        result.Add(new TextSpan(inserted, GetStyle(span)));
                        foundInsertStyle = true;
                    }
                    else if (offset == deleteEnd)
                    {
                        result.Add(new TextSpan(inserted, GetStyle(span)));
                        result.Add(span);
                        foundInsertStyle = true;
                    }
                    else
                        result.Add(span);
                }
                else
                    result.Add(span);
            }
            else
            {
                if (span is TextSpan t)
                {
                    if (offset < deleteStart)
                        result.Add(new TextSpan(t.Text[..(deleteStart - offset)], t.Style));

                    if (!foundInsertStyle)
                    {
                        insertStyle = t.Style;
                        foundInsertStyle = true;
                        if (inserted.Length > 0)
                            result.Add(new TextSpan(inserted, t.Style));
                    }

                    if (runEnd > deleteEnd)
                        result.Add(new TextSpan(t.Text[(deleteEnd - offset)..], t.Style));
                }
                else
                {
                    if (!foundInsertStyle)
                    {
                        insertStyle = ((EquationSpan)span).Style;
                        foundInsertStyle = true;
                        if (inserted.Length > 0)
                            result.Add(new TextSpan(inserted, insertStyle));
                    }
                }
            }

            offset = runEnd;
        }

        if (!foundInsertStyle && inserted.Length > 0)
            result.Add(new TextSpan(inserted, TextStyle.Default));

        if (result.Count == 0)
            result.Add(InlineSpan.Plain(string.Empty));

        return Normalize(result);
    }

    private static List<InlineSpan> SplitAtBoundaries(IReadOnlyList<InlineSpan> spans, int start, int end)
    {
        var result = new List<InlineSpan>();
        int offset = 0;

        foreach (var span in spans)
        {
            int runEnd = offset + SpanLen(span);
            int splitStart = start - offset;
            int splitEnd = end - offset;

            if (span is TextSpan t)
            {
                if (splitStart > 0 && splitStart < t.Text.Length && splitEnd > 0)
                {
                    result.Add(new TextSpan(t.Text[..splitStart], t.Style));
                    if (splitEnd > splitStart && splitEnd < t.Text.Length)
                    {
                        result.Add(new TextSpan(t.Text[splitStart..splitEnd], t.Style));
                        result.Add(new TextSpan(t.Text[splitEnd..], t.Style));
                    }
                    else
                        result.Add(new TextSpan(t.Text[splitStart..], t.Style));
                }
                else if (splitEnd > 0 && splitEnd < t.Text.Length && splitStart <= 0)
                {
                    result.Add(new TextSpan(t.Text[..splitEnd], t.Style));
                    result.Add(new TextSpan(t.Text[splitEnd..], t.Style));
                }
                else
                    result.Add(span);
            }
            else
                result.Add(span);

            offset = runEnd;
        }

        return result;
    }

    private static List<InlineSpan> ApplyEquation(
        IReadOnlyList<InlineSpan> spans, int start, int end, string? latex)
    {
        var selected = SliceRuns(spans, start, end);
        if (selected.Count == 1 && selected[0] is EquationSpan e)
        {
            return ReplaceRange(spans, start, end,
                new List<InlineSpan> { InlineSpan.Plain(e.Latex) });
        }

        var source = latex ?? EquationLatexNormalizer.Normalize(Flatten(selected));
        return ReplaceRange(spans, start, end, new List<InlineSpan> { new EquationSpan(source) });
    }

    private static bool AllSpansInRangeHaveFormat(
        IReadOnlyList<InlineSpan> spans, int start, int end, InlineFormatKind kind, string? color)
    {
        int offset = 0;
        foreach (var span in spans)
        {
            int runEnd = offset + SpanLen(span);
            if (offset < end && runEnd > start)
            {
                var st = GetStyle(span);
                if (kind == InlineFormatKind.BackgroundColor)
                {
                    if (st.BackgroundColor != color)
                        return false;
                }
                else if (!st.Has(kind))
                    return false;
            }

            offset = runEnd;
        }

        return true;
    }
}
