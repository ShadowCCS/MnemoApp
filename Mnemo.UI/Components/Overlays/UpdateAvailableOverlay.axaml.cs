using Avalonia.Markup.Xaml;
using Mnemo.UI.Modules.Updates.ViewModels;

namespace Mnemo.UI.Components.Overlays;

public partial class UpdateAvailableOverlay : Avalonia.Controls.UserControl
{
    public UpdateAvailableOverlay()
    {
        InitializeComponent();
    }

    public UpdateAvailableOverlay(UpdateAvailableViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
