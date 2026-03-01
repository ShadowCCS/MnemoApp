using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Mnemo.UI.Components.BlockEditor;

public class FocusManager
{
    private readonly Control _parentControl;
    private TextBox? _cachedTextBox;

    public FocusManager(Control parentControl)
    {
        _parentControl = parentControl ?? throw new ArgumentNullException(nameof(parentControl));
    }

    public void FocusTextBox(int? caretPosition = null)
    {
        // Use Input priority so focus transfers before Avalonia renders the focus-loss
        // state on the previously focused TextBox. Loaded priority would let a frame render
        // with the old block's caret snapped to 0, causing a visible flicker.
        // If the TextBox isn't in the tree yet (e.g. newly created block), fall back to Loaded.
        Dispatcher.UIThread.Post(() =>
        {
            var textBox = FindFocusableTextBox();
            if (textBox == null)
            {
                // TextBox not yet rendered — retry at Loaded priority
                Dispatcher.UIThread.Post(() =>
                {
                    var tb = FindFocusableTextBox();
                    if (tb == null) return;
                    ApplyFocus(tb, caretPosition);
                }, DispatcherPriority.Loaded);
                return;
            }

            ApplyFocus(textBox, caretPosition);
        }, DispatcherPriority.Input);
    }

    private void ApplyFocus(TextBox textBox, int? caretPosition)
    {
        // Set CaretIndex before Focus() so the caret is already at the correct position
        // when the TextBox first renders its focused state — prevents a snap flicker.
        var targetCaret = caretPosition ?? (textBox.Text?.Length ?? 0);
        textBox.CaretIndex = targetCaret;
        textBox.Focus();
        _cachedTextBox = textBox;
    }

    public void FocusTextBoxAtStart()
    {
        FocusTextBox(0);
    }

    public void FocusTextBoxAtEnd()
    {
        FocusTextBox(null);
    }

    public TextBox? GetFocusedTextBox()
    {
        return _parentControl.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(tb => tb.IsFocused);
    }

    public TextBox? GetCurrentTextBox()
    {
        // Try cached first - verify it's still valid
        if (_cachedTextBox != null 
            && _cachedTextBox.IsVisible 
            && _cachedTextBox.IsEffectivelyVisible 
            && _cachedTextBox.GetVisualRoot() != null)
        {
            return _cachedTextBox;
        }

        // Find visible textbox and update cache
        var textBox = FindFocusableTextBox();
        _cachedTextBox = textBox;
        return textBox;
    }

    public void ClearCache()
    {
        _cachedTextBox = null;
    }

    private TextBox? FindFocusableTextBox()
    {
        var textBoxes = _parentControl.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(tb => tb.IsVisible && tb.IsEffectivelyVisible)
            .ToList();

        return textBoxes.FirstOrDefault();
    }
}


