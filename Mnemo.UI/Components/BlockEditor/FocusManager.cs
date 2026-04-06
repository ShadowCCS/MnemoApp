using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
            var imageCaption = FindImageCaptionEditor();
            // Image caption is optional chrome: default block focus is HoverHost (keyboard delete, etc.).
            // Only move caret into the caption when a concrete index was requested (e.g. PendingCaretIndex).
            if (caretPosition.HasValue && imageCaption != null)
            {
                ApplyFocus(imageCaption, caretPosition);
                return;
            }

            var editor = FindFocusableEditor(excludeImageCaption: imageCaption != null);
            if (editor == null)
            {
                var imageHost = FindImageBlockHoverHost();
                if (imageHost != null)
                {
                    if (!ShouldSkipImageHoverHostFocus(imageHost))
                        imageHost.Focus();
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    var cap = FindImageCaptionEditor();
                    var e2 = FindFocusableEditor(excludeImageCaption: cap != null);
                    if (e2 != null)
                    {
                        ApplyFocus(e2, caretPosition);
                        return;
                    }
                    var h = FindImageBlockHoverHost();
                    if (h != null && !ShouldSkipImageHoverHostFocus(h))
                        h.Focus();
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
        // Don't overwrite selection when it was set by cross-block drag (FocusTextBox is posted on IsFocused, so it can run after PointerMoved has already set a range).
        if (editor.SelectionStart == editor.SelectionEnd)
        {
            editor.SelectionStart = targetCaret;
            editor.SelectionEnd = targetCaret;
        }
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

    private static bool IsImageCaptionEditor(RichTextEditor e) =>
        e.Tag is string t && t == "BlockEditorImageCaption";

    private RichTextEditor? FindImageCaptionEditor()
    {
        return _parentControl.GetVisualDescendants()
            .OfType<RichTextEditor>()
            .FirstOrDefault(e => e.IsVisible && e.IsEffectivelyVisible && IsImageCaptionEditor(e));
    }

    private RichTextEditor? FindFocusableEditor(bool excludeImageCaption = false)
    {
        return _parentControl.GetVisualDescendants()
            .OfType<RichTextEditor>()
            .FirstOrDefault(e =>
                e.IsVisible
                && e.IsEffectivelyVisible
                && (!excludeImageCaption || !IsImageCaptionEditor(e)));
    }

    /// <summary>
    /// Image blocks: default keyboard focus target (caption is focused only on click or explicit <c>PendingCaretIndex</c>).
    /// </summary>
    private Control? FindImageBlockHoverHost()
    {
        return _parentControl.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Name == "HoverHost" && c.Focusable && c.IsVisible && c.IsEffectivelyVisible);
    }

    /// <summary>
    /// <see cref="FocusTextBox"/> is posted on <c>IsFocused</c>; by then toolbar/caption may already
    /// have keyboard focus. Moving focus to HoverHost breaks the first Button press / Flyout open.
    /// </summary>
    private bool ShouldSkipImageHoverHostFocus(Visual imageHost)
    {
        var topLevel = TopLevel.GetTopLevel(_parentControl);
        if (topLevel?.FocusManager?.GetFocusedElement() is not Visual focused)
            return false;
        return imageHost.IsVisualAncestorOf(focused);
    }
}
