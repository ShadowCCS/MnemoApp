using Avalonia.Controls;
using Avalonia.Interactivity;
using Mnemo.UI.Modules.Onboarding.ViewModels;

namespace Mnemo.UI.Modules.Onboarding.Views;

public partial class AiModelSetupPanel : UserControl
{
    public AiModelSetupPanel()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiModelSetupViewModel vm)
            vm.Detach();
    }
}
