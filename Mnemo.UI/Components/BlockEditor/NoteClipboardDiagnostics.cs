using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Opt-in clipboard tracing. Set environment variable <c>MNEMO_CLIPBOARD_LOG=1</c> before launch;
/// output goes to <see cref="Trace"/> (Debug / dotnet trace listeners).
/// </summary>
internal static class NoteClipboardDiagnostics
{
    internal static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("MNEMO_CLIPBOARD_LOG"), "1", StringComparison.OrdinalIgnoreCase);

    internal static void Log(string message)
    {
        if (!IsEnabled) return;
        Trace.WriteLine("[Mnemo:Clipboard] " + message);
    }

    internal static string SummarizeSpans(IReadOnlyList<InlineSpan>? spans, int maxSpans = 12)
    {
        if (spans == null || spans.Count == 0) return "(no spans)";
        var sb = new StringBuilder();
        int n = Math.Min(spans.Count, maxSpans);
        for (int i = 0; i < n; i++)
        {
            var s = spans[i];
            if (s is TextSpan r)
            {
                var t = r.Text.Length > 24 ? r.Text[..24] + "…" : r.Text;
                t = t.Replace("\r", "\\r").Replace("\n", "\\n", StringComparison.Ordinal);
                sb.Append('[').Append(i).Append("] text:\"")
                    .Append(t).Append("\" B=").Append(r.Style.Bold)
                    .Append(" I=").Append(r.Style.Italic)
                    .Append(" U=").Append(r.Style.Underline)
                    .Append(" ~=").Append(r.Style.Strikethrough)
                    .Append(" code=").Append(r.Style.Code)
                    .Append(" bg=").Append(r.Style.BackgroundColor ?? "—")
                    .Append("; ");
            }
            else if (s is EquationSpan e)
            {
                var t = e.Latex.Length > 24 ? e.Latex[..24] + "…" : e.Latex;
                sb.Append('[').Append(i).Append("] eq:$").Append(t).Append("$; ");
            }
            else if (s is FractionSpan f)
            {
                sb.Append('[').Append(i).Append("] frac:")
                    .Append(f.Numerator).Append('/').Append(f.Denominator).Append("; ");
            }
        }

        if (spans.Count > maxSpans)
            sb.Append("…(+").Append(spans.Count - maxSpans).Append(" spans)");
        return sb.ToString();
    }
}
