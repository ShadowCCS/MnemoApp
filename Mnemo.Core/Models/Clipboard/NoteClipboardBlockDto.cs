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
}
