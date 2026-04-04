using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace Mnemo.UI.Services;

public interface INoteClipboardPlatformService
{
    /// <param name="clipboardBitmap">Optional raster for CF_BITMAP / PNG interchange (e.g. paste into browser).</param>
    Task WriteAsync(IClipboard clipboard, string markdown, string mnemoJson, Bitmap? clipboardBitmap = null);
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
