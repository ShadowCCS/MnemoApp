using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Services;

public sealed class NoteClipboardPlatformService : INoteClipboardPlatformService
{
    private static readonly DataFormat<string> MnemoJsonDataFormat =
        DataFormat.CreateStringApplicationFormat(NoteClipboardFormats.MnemoNoteBlocksJson);

    public async Task WriteAsync(IClipboard clipboard, string markdown, string mnemoJson, Bitmap? clipboardBitmap = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        // Keep text + custom JSON on their own item. Putting DIB/PNG on the same Win32 IDataObject as CF_UNICODETEXT
        // often drops our private format or plain text; a second item preserves Mnemo↔Mnemo paste + image for other apps.
        var transfer = new DataTransfer();
        var textItem = new DataTransferItem();
        textItem.Set(DataFormat.Text, markdown);
        textItem.Set(MnemoJsonDataFormat, mnemoJson);
        transfer.Add(textItem);
        if (clipboardBitmap != null)
        {
            var bmpItem = new DataTransferItem();
            bmpItem.SetBitmap(clipboardBitmap);
            transfer.Add(bmpItem);
        }

        await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        NoteClipboardDiagnostics.Log(
            $"Write: markdownLen={markdown?.Length ?? 0} jsonLen={mnemoJson?.Length ?? 0}");
        try
        {
            await clipboard.FlushAsync().ConfigureAwait(true);
        }
        catch
        {
            // Flush is best-effort (not all platforms); Mnemo↔Mnemo paste still uses in-process data when available.
        }
    }

    public async Task<NoteClipboardReadData> ReadAsync(IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        string? mnemo = null;
        string? text = null;

        try
        {
            var inProc = await clipboard.TryGetInProcessDataAsync().ConfigureAwait(true);
            if (inProc != null)
            {
                try
                {
                    mnemo = await inProc.TryGetValueAsync(MnemoJsonDataFormat).ConfigureAwait(true);
                }
                catch
                {
                    // ignore
                }

                try
                {
                    text = await inProc.TryGetTextAsync().ConfigureAwait(true);
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            var bundle = await clipboard.TryGetDataAsync().ConfigureAwait(true);
            if (bundle != null)
            {
                try
                {
                    mnemo ??= await bundle.TryGetValueAsync(MnemoJsonDataFormat).ConfigureAwait(true);
                    text ??= await bundle.TryGetTextAsync().ConfigureAwait(true);
                }
                finally
                {
                    bundle.Dispose();
                }
            }
        }
        catch
        {
            // fall through to clipboard extension fallbacks
        }

        try
        {
            text ??= await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }
        catch
        {
            text ??= null;
        }

        if (mnemo == null)
        {
            try
            {
                mnemo = await clipboard.TryGetValueAsync(MnemoJsonDataFormat).ConfigureAwait(true);
            }
            catch
            {
                mnemo = null;
            }
        }

        NoteClipboardDiagnostics.Log(
            $"Read: mnemoJson={(mnemo != null ? $"len={mnemo.Length}" : "null")} textLen={text?.Length ?? 0}");
        return new NoteClipboardReadData(mnemo, text);
    }
}
