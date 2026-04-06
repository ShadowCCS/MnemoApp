using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Onboarding.Views;

public partial class AiModelsSetupOverlay : UserControl
{
    public string? OverlayId { get; set; }

    public AiModelsSetupOverlay()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(OverlayId))
            return;
        if (Avalonia.Application.Current is App app && app.Services?.GetService(typeof(IOverlayService)) is IOverlayService overlays)
            overlays.CloseOverlay(OverlayId);
    }
}
