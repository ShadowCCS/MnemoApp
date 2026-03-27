using System.Text.Json.Serialization;

namespace Mnemo.UI.Modules.Notes;

/// <summary>Parameters for the <c>list_notes</c> AI tool.</summary>
public sealed class ListNotesParameters
{
    /// <summary>Optional case-insensitive substring match on title and body.</summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>Max notes to return (default 30, max 100).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}
