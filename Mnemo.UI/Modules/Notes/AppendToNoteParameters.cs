using System.Text.Json.Serialization;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Parameters for the <c>append_to_note</c> AI tool.</summary>
public sealed class AppendToNoteParameters
{
    [JsonPropertyName("note_id")]
    public string NoteId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
