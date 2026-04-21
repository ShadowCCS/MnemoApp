namespace Mnemo.Core.Models.Clipboard;

/// <summary>JSON-friendly inline span for note clipboard interchange.</summary>
public sealed class NoteClipboardRunDto
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public bool Code { get; set; }
    public bool Highlight { get; set; }
    public string? BackgroundColor { get; set; }
    /// <summary>LaTeX source for inline equation runs (U+FFFC placeholder text).</summary>
    public string? EquationLatex { get; set; }
    /// <summary>Numerator for inline fraction runs.</summary>
    public int? FractionNumerator { get; set; }
    /// <summary>Denominator for inline fraction runs.</summary>
    public int? FractionDenominator { get; set; }
}
