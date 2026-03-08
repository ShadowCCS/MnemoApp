using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Mnemo.UI.Components.BlockEditor;

public class FocusManager
{
    private readonly Control _parentControl;
    private RichTextEditor? _cachedEditor;

    public FocusManager(Control parentControl)
    {
        _parentControl = parentControl ?? throw new ArgumentNullException(nameof(parentControl));
    }

    public void FocusTextBox(int? caretPosition = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var editor = FindFocusableEditor();
            if (editor == null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var e2 = FindFocusableEditor();
                    if (e2 == null) return;
                    ApplyFocus(e2, caretPosition);
                }, DispatcherPriority.Loaded);
                return;
            }
            ApplyFocus(editor, caretPosition);
        }, DispatcherPriority.Input);
    }

    private void ApplyFocus(RichTextEditor editor, int? caretPosition)
    {
        var targetCaret = caretPosition ?? editor.TextLength;
        editor.CaretIndex = targetCaret;
        editor.SelectionStart = targetCaret;
        editor.SelectionEnd = targetCaret;
        editor.Focus();
        _cachedEditor = editor;
    }

    public void FocusTextBoxAtStart() => FocusTextBox(0);
    public void FocusTextBoxAtEnd() => FocusTextBox(null);

    /// <summary>Returns the currently focused <see cref="RichTextEditor"/>, or null.</summary>
    public RichTextEditor? GetFocusedTextBox()
    {
        return _parentControl.GetVisualDescendants()
            .OfType<RichTextEditor>()
            .FirstOrDefault(e => e.IsFocused);
    }

    /// <summary>Returns the cached or discovered <see cref="RichTextEditor"/>.</summary>
    public RichTextEditor? GetCurrentTextBox()
    {
        if (_cachedEditor != null
            && _cachedEditor.IsVisible
            && _cachedEditor.IsEffectivelyVisible
            && _cachedEditor.GetVisualRoot() != null)
            return _cachedEditor;

        var editor = FindFocusableEditor();
        _cachedEditor = editor;
        return editor;
    }

    public void ClearCache() => _cachedEditor = null;

    private RichTextEditor? FindFocusableEditor()
    {
        return _parentControl.GetVisualDescendants()
            .OfType<RichTextEditor>()
            .FirstOrDefault(e => e.IsVisible && e.IsEffectivelyVisible);
    }
}
