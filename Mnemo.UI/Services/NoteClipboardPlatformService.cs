using Avalonia.Input;
using Avalonia.Input.Platform;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Services;

public sealed class NoteClipboardPlatformService : INoteClipboardPlatformService
{
    private static readonly DataFormat<string> MnemoJsonDataFormat =
        DataFormat.CreateStringApplicationFormat(NoteClipboardFormats.MnemoNoteBlocksJson);

    public async Task WriteAsync(IClipboard clipboard, string markdown, string mnemoJson)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        // One item with both plain-text (interchange) and Mnemo JSON (full runs) — matches Avalonia clipboard guidance.
        var item = new DataTransferItem();
        item.Set(DataFormat.Text, markdown);
        item.Set(MnemoJsonDataFormat, mnemoJson);
        var transfer = new DataTransfer();
        transfer.Add(item);
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
                mnemo = await inProc.TryGetValueAsync(MnemoJsonDataFormat).ConfigureAwait(true);
        }
        catch
        {
            mnemo = null;
        }

        try
        {
            var bundle = await clipboard.TryGetDataAsync().ConfigureAwait(true);
            if (bundle != null)
            {
                try
                {
                    mnemo ??= await bundle.TryGetValueAsync(MnemoJsonDataFormat).ConfigureAwait(true);
                    text = await bundle.TryGetTextAsync().ConfigureAwait(true);
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
