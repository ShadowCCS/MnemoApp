namespace Mnemo.UI.Components.BlockEditor;

public static class NoteClipboardFormats
{
    /// <summary>
    /// Application clipboard id for structured note payload (UTF-8 JSON).
    /// Must match Avalonia rules: only A–Z, a–z, 0–9, dot, hyphen (no slashes or '+').
    /// </summary>
    public const string MnemoNoteBlocksJson = "mnemo.noteblocks.v1";
}
