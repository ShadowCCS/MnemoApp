using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mnemo.UI.Components.BlockEditor;
using System;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents;

/// <summary>
/// Base class for all block components with shared event handling.
/// Subclasses only need to call <see cref="WireInputControl"/> after InitializeComponent()
/// and override <see cref="GetInputControl"/>. Unsubscription is handled here automatically.
/// </summary>
public abstract class BlockComponentBase : UserControl
{
    private TextBox? _wiredTextBox;

    protected BlockViewModel? ViewModel => DataContext as BlockViewModel;
    
    /// <summary>
    /// Gets the primary input control for this block (TextBox, CheckBox, etc.)
    /// </summary>
    public abstract Control? GetInputControl();

    /// <summary>
    /// Wires the standard TextBox event handlers. Call this from the subclass constructor
    /// after InitializeComponent() when the block has a TextBox input.
    /// </summary>
    protected void WireInputControl(TextBox textBox)
    {
        _wiredTextBox = textBox;
        textBox.GotFocus += OnTextBoxGotFocus;
        textBox.LostFocus += OnTextBoxLostFocus;
        textBox.TextChanged += OnTextBoxTextChanged;
        textBox.KeyDown += OnTextBoxKeyDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_wiredTextBox != null)
        {
            _wiredTextBox.GotFocus -= OnTextBoxGotFocus;
            _wiredTextBox.LostFocus -= OnTextBoxLostFocus;
            _wiredTextBox.TextChanged -= OnTextBoxTextChanged;
            _wiredTextBox.KeyDown -= OnTextBoxKeyDown;
            _wiredTextBox = null;
        }
    }

    /// <summary>Event raised when the TextBox receives focus.</summary>
    public event EventHandler<TextBox>? TextBoxGotFocus;

    /// <summary>Event raised when the TextBox text changes.</summary>
    public event EventHandler<TextChangedEventArgs>? TextBoxTextChanged;

    /// <summary>Event raised when a key is pressed inside the TextBox.</summary>
    public event EventHandler<KeyEventArgs>? TextBoxKeyDown;

    /// <summary>Event raised when the TextBox loses focus.</summary>
    public event EventHandler<RoutedEventArgs>? TextBoxLostFocus;

    protected void OnTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            TextBoxGotFocus?.Invoke(this, textBox);
    }

    protected void OnTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        TextBoxTextChanged?.Invoke(this, e);
    }

    protected void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        TextBoxKeyDown?.Invoke(this, e);
    }

    protected void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        TextBoxLostFocus?.Invoke(this, e);
    }
}


