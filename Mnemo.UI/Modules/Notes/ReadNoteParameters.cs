using System.Text.Json.Serialization;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Parameters for the <c>read_note</c> AI tool.</summary>
public sealed class ReadNoteParameters
{
    [JsonPropertyName("note_id")]
    public string NoteId { get; set; } = string.Empty;
}
