using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Mnemo.UI.Components;

public partial class Topbar : UserControl
{
    public Topbar()
    {
        InitializeComponent();
    }

    private void MaximizeWindow(object? sender, RoutedEventArgs e)
    {
        var window = this.GetVisualRoot() as Window;
        if (window == null)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        var window = this.GetVisualRoot() as Window;
        window?.Close();
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs e)
    {
        var window = this.GetVisualRoot() as Window;
        if (window == null)
            return;

        window.WindowState = WindowState.Minimized;
    }
}
