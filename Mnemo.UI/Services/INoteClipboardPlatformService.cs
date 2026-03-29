using Avalonia.Input.Platform;

namespace Mnemo.UI.Services;

public interface INoteClipboardPlatformService
{
    Task WriteAsync(IClipboard clipboard, string markdown, string mnemoJson);
    Task<NoteClipboardReadData> ReadAsync(IClipboard clipboard);
}

public readonly struct NoteClipboardReadData
{
    public NoteClipboardReadData(string? mnemoJson, string? text)
    {
        MnemoJson = mnemoJson;
        Text = text;
    }

    public string? MnemoJson { get; }
    public string? Text { get; }
}
