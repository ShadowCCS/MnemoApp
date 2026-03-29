using System.Text.Json;
using System.Text.Json.Serialization;
using Mnemo.Core.Models.Clipboard;

namespace Mnemo.Core.Services;

public sealed class NoteClipboardPayloadCodec : INoteClipboardPayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Serialize(NoteClipboardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public bool TryDeserialize(string json, out NoteClipboardDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            document = JsonSerializer.Deserialize<NoteClipboardDocument>(json.Trim(), Options);
            if (document?.Blocks == null || document.SchemaVersion < 1)
                return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
