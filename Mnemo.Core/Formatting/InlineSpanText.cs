using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.Core.Formatting;

/// <summary>Flattened string for caret indices / text diff: one <see cref="InlineSpan.EquationAtomChar"/> per <see cref="EquationSpan"/>.</summary>
public static class InlineSpanText
{
    public static int LogicalLength(IReadOnlyList<InlineSpan> spans)
    {
        int n = 0;
        foreach (var s in spans)
            n += s is TextSpan t ? t.Text.Length : 1;
        return n;
    }

    public static string FlattenEditing(IReadOnlyList<InlineSpan> spans)
    {
        if (spans.Count == 0) return string.Empty;
        if (spans.Count == 1 && spans[0] is TextSpan one)
            return one.Text;
        var sb = new StringBuilder();
        foreach (var s in spans)
        {
            if (s is TextSpan t)
                sb.Append(t.Text);
            else if (s is EquationSpan)
                sb.Append(InlineSpan.EquationAtomChar);
        }

        return sb.ToString();
    }

    public static string FlattenDisplay(IReadOnlyList<InlineSpan> spans) =>
        string.Concat(spans.Select(s => s is TextSpan t ? t.Text : s is EquationSpan e ? e.Latex : string.Empty));
}
