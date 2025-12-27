using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mnemo.UI.Components.BlockEditor;
using System;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents;

/// <summary>
/// Base class for all block components with shared event handling.
/// </summary>
public abstract class BlockComponentBase : UserControl
{
    protected BlockViewModel? ViewModel => DataContext as BlockViewModel;
    
    /// <summary>
    /// Gets the primary input control for this block (TextBox, CheckBox, etc.)
    /// </summary>
    public abstract Control? GetInputControl();
    
    /// <summary>
    /// Event raised when the block needs to handle focus events
    /// </summary>
    public event EventHandler<TextBox>? TextBoxGotFocus;
    
    /// <summary>
    /// Event raised when the block needs to handle text changes
    /// </summary>
    public event EventHandler<TextChangedEventArgs>? TextBoxTextChanged;
    
    /// <summary>
    /// Event raised when the block needs to handle key down events
    /// </summary>
    public event EventHandler<KeyEventArgs>? TextBoxKeyDown;
    
    /// <summary>
    /// Event raised when the block loses focus
    /// </summary>
    public event EventHandler<RoutedEventArgs>? TextBoxLostFocus;

    protected void OnTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            TextBoxGotFocus?.Invoke(this, textBox);
        }
    }

    protected void OnTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        TextBoxTextChanged?.Invoke(this, e);
    }

    protected void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[BlockComponentBase] OnTextBoxKeyDown - Key: {e.Key}, Handled: {e.Handled}");
        TextBoxKeyDown?.Invoke(this, e);
    }

    protected void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        TextBoxLostFocus?.Invoke(this, e);
    }
}


