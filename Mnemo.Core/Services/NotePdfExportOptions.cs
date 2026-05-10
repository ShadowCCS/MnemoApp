using System.Collections.Generic;

namespace Mnemo.Core.Services;

public enum NotePdfPaperKind
{
    A4,
    Letter
}

public enum NotePdfMarginPreset
{
    Normal,
    Narrow
}

public enum NotePdfPageNumberAlignment
{
    None,
    Left,
    Center,
    Right
}

public enum NotePdfPageNumberFormat
{
    CurrentPage,
    CurrentAndTotalPages
}

/// <summary>Options for exporting a note to PDF. Used by <see cref="INotePdfExportService"/>.</summary>
public sealed class NotePdfExportOptions
{
    public NotePdfPaperKind Paper { get; init; } = NotePdfPaperKind.A4;

    public NotePdfMarginPreset Margin { get; init; } = NotePdfMarginPreset.Normal;

    public bool IncludeNoteTitle { get; init; } = true;

    public float BaseFontSizePt { get; init; } = 11f;

    public NotePdfPageNumberAlignment PageNumberAlignment { get; init; } = NotePdfPageNumberAlignment.Center;

    public NotePdfPageNumberFormat PageNumberFormat { get; init; } = NotePdfPageNumberFormat.CurrentAndTotalPages;

    /// <summary>DPI for preview rasterization only (<see cref="INotePdfExportService.GeneratePreviewPngPagesAsync"/>).</summary>
    public int PreviewRasterDpi { get; init; } = 120;

    /// <summary>
    /// Maps inline background keys from the notes editor (e.g. <c>swatch1</c>) to <c>#RRGGBB</c> for PDF rendering.
    /// Should use the <b>Dawn</b> theme swatch table so colors match a light page regardless of the app theme.
    /// When null, only literal hex strings in span styles resolve.
    /// </summary>
    public IReadOnlyDictionary<string, string>? BackgroundSwatchHexByName { get; init; }
}
