using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Mnemo.UI.Components.BlockEditor;

namespace Mnemo.UI.Services;

public sealed class BlockEditorClipboardKeybindDispatch : IBlockEditorClipboardKeybindDispatch
{
    public bool TryCopy() =>
        FindBlockEditor()?.TryHandleCopyKeybind() ?? false;

    public bool TryCut() =>
        FindBlockEditor()?.TryHandleCutKeybind() ?? false;

    public bool TryPaste() =>
        FindBlockEditor()?.TryHandlePasteKeybind() ?? false;

    private static BlockEditor? FindBlockEditor()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        if (desktop.MainWindow?.FocusManager?.GetFocusedElement() is not Visual focused)
            return null;

        for (var v = focused; v != null; v = v.GetVisualParent())
        {
            if (v is BlockEditor be)
                return be;
        }

        return null;
    }
}
