using Mnemo.Core.Models.Clipboard;

namespace Mnemo.Core.Services;

public interface INoteClipboardPayloadCodec
{
    string Serialize(NoteClipboardDocument document);
    bool TryDeserialize(string json, out NoteClipboardDocument? document);
}
