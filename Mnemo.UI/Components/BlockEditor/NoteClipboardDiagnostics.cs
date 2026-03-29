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

    internal static string SummarizeRuns(IReadOnlyList<InlineRun>? runs, int maxRuns = 12)
    {
        if (runs == null || runs.Count == 0) return "(no runs)";
        var sb = new StringBuilder();
        int n = Math.Min(runs.Count, maxRuns);
        for (int i = 0; i < n; i++)
        {
            var r = runs[i];
            var t = r.Text.Length > 24 ? r.Text[..24] + "…" : r.Text;
            t = t.Replace("\r", "\\r").Replace("\n", "\\n", StringComparison.Ordinal);
            sb.Append('[').Append(i).Append("] \"")
                .Append(t).Append("\" B=").Append(r.Style.Bold)
                .Append(" I=").Append(r.Style.Italic)
                .Append(" U=").Append(r.Style.Underline)
                .Append(" ~=").Append(r.Style.Strikethrough)
                .Append(" code=").Append(r.Style.Code)
                .Append(" bg=").Append(r.Style.BackgroundColor ?? "—")
                .Append("; ");
        }

        if (runs.Count > maxRuns)
            sb.Append("…(+").Append(runs.Count - maxRuns).Append(" runs)");
        return sb.ToString();
    }
}
