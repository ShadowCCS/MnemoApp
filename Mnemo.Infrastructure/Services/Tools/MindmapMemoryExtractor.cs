using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>
/// Extracts conversation-memory facts from Mindmap tool results.
/// Rule-based and synchronous — no LLM call.
/// </summary>
public sealed class MindmapMemoryExtractor : IToolResultMemoryExtractor
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

        var now = DateTime.UtcNow;
        root.TryGetProperty("data", out var data);

        switch (toolName.ToLowerInvariant())
        {
            case "create_mindmap":
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined)
                {
                    if (TryGetString(data, "mindmap_id", out var mmId))
                        yield return MakeFact("active_mindmap_id", mmId!, toolName, turnNumber, now);
                    if (TryGetString(data, "title", out var title))
                        yield return MakeFact("active_mindmap_title", title!, toolName, turnNumber, now);
                }
                break;
            }

            case "read_mindmap":
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined)
                {
                    if (TryGetString(data, "mindmap_id", out var mmId))
                        yield return MakeFact("active_mindmap_id", mmId!, toolName, turnNumber, now);
                    if (TryGetString(data, "title", out var title))
                        yield return MakeFact("active_mindmap_title", title!, toolName, turnNumber, now);
                }
                break;
            }

            case "list_mindmaps":
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined
                    && data.TryGetProperty("mindmaps", out var maps) && maps.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<string>();
                    foreach (var m in maps.EnumerateArray())
                    {
                        if (TryGetString(m, "mindmap_id", out var id))
                            ids.Add(id!);
                    }
                    if (ids.Count > 0)
                        yield return MakeFact("listed_mindmap_ids",
                            JsonSerializer.Serialize(ids), toolName, turnNumber, now);
                }
                break;
            }

            case "add_nodes":
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined
                    && data.TryGetProperty("node_ids", out var nodeIds) && nodeIds.ValueKind == JsonValueKind.Array)
                {
                    var last = nodeIds.EnumerateArray().LastOrDefault();
                    if (last.ValueKind == JsonValueKind.String)
                    {
                        var s = last.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            yield return MakeFact("last_added_node_id", s!, toolName, turnNumber, now);
                    }
                }
                break;
            }

            case "connect_nodes":
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined
                    && TryGetString(data, "edge_id", out var edgeId))
                {
                    yield return MakeFact("last_added_edge_id", edgeId!, toolName, turnNumber, now);
                }
                break;
            }

            case "open_mindmap":
            {
                if (TryGetString(root, "message", out var msg) && msg != null)
                {
                    var extracted = ExtractIdFromMessage(msg);
                    if (extracted != null)
                        yield return MakeFact("active_mindmap_id", extracted, toolName, turnNumber, now);
                }
                break;
            }
        }
    }

    private static string? ExtractIdFromMessage(string message)
    {
        var start = message.IndexOf("id: ", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
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
