using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Models.Clipboard;

/// <summary>JSON-friendly block for note clipboard interchange.</summary>
public sealed class NoteClipboardBlockDto
{
    public BlockType Type { get; set; }
    /// <summary>Optional plain fallback when <see cref="Runs"/> is null or empty.</summary>
    public string? Content { get; set; }
    public List<NoteClipboardRunDto>? Runs { get; set; }
    public bool? IsChecked { get; set; }
    public int? ListNumberIndex { get; set; }
    /// <summary>Code block language (optional).</summary>
    public string? CodeLanguage { get; set; }

    /// <summary>Stored image path for <see cref="BlockType.Image"/> (app-local or absolute).</summary>
    public string? ImagePath { get; set; }

    /// <summary>Caption / alt text for image blocks.</summary>
    public string? ImageAlt { get; set; }

    /// <summary>Display width in layout units; 0 means natural size.</summary>
    public double? ImageWidth { get; set; }

    /// <summary>Horizontal layout for image blocks: left, center, or right.</summary>
    public string? ImageAlign { get; set; }

    /// <summary>Legacy clipboard: nested blocks for <see cref="BlockType.TwoColumn"/>.</summary>
    public List<NoteClipboardBlockDto>? Children { get; set; }

    /// <summary>Side-by-side pair id (left block carries <see cref="ColumnSplitRatio"/>).</summary>
    public string? ColumnPairId { get; set; }

    public string? ColumnSide { get; set; }

    /// <summary>Left column width fraction when <see cref="ColumnSide"/> is Left.</summary>
    public double? ColumnSplitRatio { get; set; }

    /// <summary>LaTeX source for <see cref="BlockType.Equation"/> blocks.</summary>
    public string? EquationLatex { get; set; }
}
