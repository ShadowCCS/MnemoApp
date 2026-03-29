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
}
