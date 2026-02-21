using Avalonia.Controls;
using Avalonia.Input;

namespace Mnemo.UI.Components.RightSidebar;

public partial class RightSidebar : UserControl
{
    public RightSidebar()
    {
        InitializeComponent();
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
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
