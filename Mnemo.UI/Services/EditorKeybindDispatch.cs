using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Mnemo.Core.Formatting;
using Mnemo.UI.Components.BlockEditor;
using Mnemo.UI.Controls;

namespace Mnemo.UI.Services;

public sealed class EditorKeybindDispatch : IEditorKeybindDispatch
{
    public void Apply(InlineFormatKind kind)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        if (desktop.MainWindow?.FocusManager?.GetFocusedElement() is not Visual focused)
            return;

        for (var v = focused; v != null; v = v.GetVisualParent())
        {
            switch (v)
            {
                case RichDocumentEditor doc:
                    doc.TryApplyKeybindFormat(kind);
                    return;
                case RichTextEditor rte:
                    if (rte.FindAncestorOfType<RichDocumentEditor>() is { } rd)
                    {
                        rd.TryApplyKeybindFormat(kind);
                        return;
                    }

                    if (rte.FindAncestorOfType<EditableBlock>() is { } eb)
                        eb.TryApplyEditorKeybind(kind, rte);
                    return;
            }
        }
    }
}
