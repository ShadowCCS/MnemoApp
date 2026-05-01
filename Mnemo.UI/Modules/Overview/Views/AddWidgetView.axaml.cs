using Avalonia.Controls;
using Avalonia.Interactivity;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Views;

public partial class AddWidgetView : UserControl
{
    public AddWidgetView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is AddWidgetView view && view.DataContext is AddWidgetViewModel vm)
            vm.DetachLocalizationListener();
    }
}
