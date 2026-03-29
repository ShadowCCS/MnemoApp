namespace Mnemo.Core.Models.Clipboard;

/// <summary>Versioned root payload for Mnemo-to-Mnemo clipboard copy.</summary>
public sealed class NoteClipboardDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<NoteClipboardBlockDto> Blocks { get; set; } = [];
}
