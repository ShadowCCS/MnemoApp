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
        Dispatcher.UIThread.Post(() =>
        {
            var textBox = FindFocusableTextBox();
            if (textBox == null) return;

            textBox.Focus();
            
            if (caretPosition.HasValue)
            {
                textBox.CaretIndex = caretPosition.Value;
            }
            else
            {
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
            }
            
            _cachedTextBox = textBox;
        }, DispatcherPriority.Loaded);
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


