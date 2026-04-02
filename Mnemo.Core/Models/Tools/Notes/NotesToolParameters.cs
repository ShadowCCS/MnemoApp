using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Notes;

public sealed class ToolBlockPayload
{
    [JsonPropertyName("block_id")] public string? BlockId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "Text";
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("meta")] public Dictionary<string, object>? Meta { get; set; }
}

public sealed class ListNotesParameters
{
    [JsonPropertyName("search")] public string? Search { get; set; }
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    /// <summary>When true (default), typo-tolerant token match against title/body.</summary>
    [JsonPropertyName("fuzzy")] public bool? Fuzzy { get; set; }
}

public sealed class NoteIdParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
}

public sealed class CreateNoteParameters
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("blocks")] public List<ToolBlockPayload>? Blocks { get; set; }
}

public sealed class UpdateNoteParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("blocks")] public List<ToolBlockPayload>? Blocks { get; set; }
}

public sealed class AppendToNoteParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("blocks")] public List<ToolBlockPayload>? Blocks { get; set; }
}

public sealed class OpenNoteParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
}

public sealed class SearchNotesParameters
{
    [JsonPropertyName("query")] public string Query { get; set; } = string.Empty;
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    /// <summary>When true, every keyword must appear (AND). Default false (OR).</summary>
    [JsonPropertyName("match_all")] public bool? MatchAll { get; set; }
    /// <summary>When true (default), allow small edit-distance mismatches on words (e.g. Gemany vs Germany).</summary>
    [JsonPropertyName("fuzzy")] public bool? Fuzzy { get; set; }
}

public sealed class GetRecentNotesParameters
{
    [JsonPropertyName("limit")] public int? Limit { get; set; }
}

public sealed class InsertBlocksParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("blocks")] public List<ToolBlockPayload> Blocks { get; set; } = [];
    [JsonPropertyName("position")] public string Position { get; set; } = "bottom";
    [JsonPropertyName("anchor_block_id")] public string? AnchorBlockId { get; set; }
}

public sealed class ReplaceBlockParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("block_id")] public string BlockId { get; set; } = string.Empty;
    [JsonPropertyName("block")] public ToolBlockPayload Block { get; set; } = new();
}

public sealed class DeleteBlocksParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("block_ids")] public List<string> BlockIds { get; set; } = [];
}

public sealed class RestructureNoteParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("blocks")] public List<ToolBlockPayload> Blocks { get; set; } = [];
}

public sealed class ConvertBlockParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("block_id")] public string BlockId { get; set; } = string.Empty;
    [JsonPropertyName("new_type")] public string NewType { get; set; } = string.Empty;
}

public sealed class FindRelatedNotesParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("limit")] public int? Limit { get; set; }
}

public sealed class ReadNoteLinesParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("line_range")] public string? LineRange { get; set; }
}

public sealed class ReplaceNoteLinesParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("replace_lines")] public string ReplaceLines { get; set; } = string.Empty;
    [JsonPropertyName("content_markdown")] public string ContentMarkdown { get; set; } = string.Empty;
}

public sealed class InsertNoteLinesParameters
{
    [JsonPropertyName("note_id")] public string NoteId { get; set; } = string.Empty;
    [JsonPropertyName("at_line")] public int AtLine { get; set; }
    [JsonPropertyName("content_markdown")] public string ContentMarkdown { get; set; } = string.Empty;
}
