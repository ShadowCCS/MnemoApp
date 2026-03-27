using System.Text.Json.Serialization;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Parameters for the <c>update_note</c> AI tool.</summary>
public sealed class UpdateNoteParameters
{
    [JsonPropertyName("note_id")]
    public string NoteId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Replaces the entire note body (plain text / markdown).</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
