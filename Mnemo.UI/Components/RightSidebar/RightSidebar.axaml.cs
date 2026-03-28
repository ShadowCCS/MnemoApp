using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Mnemo.UI.Components.RightSidebar;

public partial class RightSidebar : UserControl
{
    private TextBox? _inputBox;
    private readonly EventHandler<KeyEventArgs> _inputBoxKeyDownHandler;

    public RightSidebar()
    {
        InitializeComponent();
        _inputBoxKeyDownHandler = InputBox_KeyDown;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputBox = this.FindControl<TextBox>("InputBox");
        _inputBox?.AddHandler(InputElement.KeyDownEvent, _inputBoxKeyDownHandler, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_inputBox != null)
        {
            _inputBox.RemoveHandler(InputElement.KeyDownEvent, _inputBoxKeyDownHandler);
            _inputBox = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (sender is not TextBox tb)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var text = tb.Text ?? string.Empty;
            var caret = Math.Clamp(tb.CaretIndex, 0, text.Length);
            tb.Text = text.Insert(caret, "\n");
            tb.CaretIndex = caret + 1;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is not RightSidebarViewModel vm)
            return;

        if (vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

}
