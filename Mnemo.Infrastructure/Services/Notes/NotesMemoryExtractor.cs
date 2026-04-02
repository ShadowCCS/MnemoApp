using System;
using System.Collections.Generic;
using System.Text.Json;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Notes;

/// <summary>
/// Extracts conversation-memory facts from Notes tool results.
/// Rule-based and synchronous — no LLM call.
/// </summary>
public sealed class NotesMemoryExtractor : IToolResultMemoryExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IEnumerable<ConversationMemoryEntry> Extract(string toolName, string resultJson, int turnNumber)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            yield break;

        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(resultJson, JsonOpts);
        }
        catch
        {
            yield break;
        }

        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            yield break;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            yield break;

        var now = DateTime.UtcNow;

        switch (toolName.ToLowerInvariant())
        {
            case "create_note":
            {
                if (TryGetString(data, "note_id", out var noteId))
                    yield return MakeFact("active_note_id", noteId!, toolName, turnNumber, now);
                if (TryGetString(data, "title", out var title))
                    yield return MakeFact("active_note_title", title!, toolName, turnNumber, now);
                break;
            }

            case "get_note":
            case "read_note":
            {
                if (TryGetString(data, "note_id", out var noteId))
                    yield return MakeFact("active_note_id", noteId!, toolName, turnNumber, now);
                if (TryGetString(data, "title", out var title))
                    yield return MakeFact("active_note_title", title!, toolName, turnNumber, now);
                break;
            }

            case "note_exists":
            {
                if (data.TryGetProperty("exists", out var existsProp) && existsProp.GetBoolean())
                {
                    if (TryGetString(data, "note_id", out var noteId))
                        yield return MakeFact("active_note_id", noteId!, toolName, turnNumber, now);
                }
                break;
            }

            case "list_notes":
            case "get_recent_notes":
            {
                if (data.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<string>();
                    foreach (var n in notes.EnumerateArray())
                    {
                        if (TryGetString(n, "id", out var id))
                            ids.Add(id!);
                    }

                    if (ids.Count > 0)
                        yield return MakeFact("listed_note_ids",
                            JsonSerializer.Serialize(ids), toolName, turnNumber, now);
                }
                break;
            }

            case "update_note":
            case "append_to_note":
            case "insert_blocks":
            case "replace_block":
            case "delete_blocks":
            case "restructure_note":
            case "convert_block":
            case "replace_note_lines":
            case "insert_note_lines":
            {
                // These tools don't return note_id in data, but the message contains it —
                // active_note_id was already set from the get/create; no update needed unless
                // we can parse it from the message.
                if (TryGetString(root, "message", out var msg) && msg != null)
                {
                    var extracted = ExtractIdFromMessage(msg);
                    if (extracted != null)
                        yield return MakeFact("active_note_id", extracted, toolName, turnNumber, now);
                }
                break;
            }

            case "open_note":
            {
                if (TryGetString(root, "message", out var msg) && msg != null)
                {
                    var extracted = ExtractIdFromMessage(msg);
                    if (extracted != null)
                        yield return MakeFact("active_note_id", extracted, toolName, turnNumber, now);
                }
                break;
            }

            case "search_notes":
            {
                if (data.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<string>();
                    foreach (var h in hits.EnumerateArray())
                    {
                        if (TryGetString(h, "note_id", out var id))
                            ids.Add(id!);
                    }
                    if (ids.Count > 0)
                        yield return MakeFact("search_result_note_ids",
                            JsonSerializer.Serialize(ids), toolName, turnNumber, now);
                }
                break;
            }
        }
    }

    private static string? ExtractIdFromMessage(string message)
    {
        // Matches "(id: <value>)" or "id: <value>" patterns in tool messages
        var start = message.IndexOf("id: ", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += 4;
        var end = message.IndexOfAny([')', ' ', '\n', '\r'], start);
        var id = end < 0 ? message[start..] : message[start..end];
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }
        value = null;
        return false;
    }

    private static ConversationMemoryEntry MakeFact(
        string key, string value, string source, int turnNumber, DateTime createdUtc) =>
        new()
        {
            Key = key,
            Value = value,
            Source = source,
            TurnNumber = turnNumber,
            CreatedUtc = createdUtc
        };
}
