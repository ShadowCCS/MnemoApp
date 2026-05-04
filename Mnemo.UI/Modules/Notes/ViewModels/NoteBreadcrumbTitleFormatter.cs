namespace Mnemo.UI.Modules.Notes.ViewModels;

internal static class NoteBreadcrumbTitleFormatter
{
    /// <summary>Max characters per breadcrumb label (including ellipsis when truncated).</summary>
    public const int MaxSegmentCharacters = 28;

    private const char EllipsisChar = '\u2026';

    public static string ToDisplayText(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var t = raw.Trim();
        if (t.Length <= MaxSegmentCharacters) return t;
        return t[..(MaxSegmentCharacters - 1)] + EllipsisChar;
    }

    /// <summary>Full title for tooltip when the label is shortened; otherwise null so no tooltip is shown.</summary>
    public static string? ToolTipFor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        return t.Length > MaxSegmentCharacters ? t : null;
    }
}
