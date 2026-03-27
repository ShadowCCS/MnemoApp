using System.Text.Json.Serialization;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Parameters for the <c>open_note</c> AI tool.</summary>
public sealed class OpenNoteParameters
{
    [JsonPropertyName("note_id")]
    public string NoteId { get; set; } = string.Empty;
}
