using System.Threading.Tasks;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Onboarding.ViewModels;
using Mnemo.UI.Modules.Onboarding.Views;

namespace Mnemo.UI.Services;

public sealed class AiSetupOverlayPresenter : IAiSetupOverlayPresenter
{
    private readonly IServiceProvider _services;
    private readonly IOverlayService _overlays;
    private readonly IMainThreadDispatcher _dispatcher;

    public AiSetupOverlayPresenter(
        IServiceProvider services,
        IOverlayService overlays,
        IMainThreadDispatcher dispatcher)
    {
        _services = services;
        _overlays = overlays;
        _dispatcher = dispatcher;
    }

    public Task ShowAsync()
    {
        var vm = _services.GetRequiredService<AiModelSetupViewModel>();
        return _dispatcher.InvokeAsync(async () =>
        {
            await vm.InitializeAsync().ConfigureAwait(false);
            var view = new AiModelsSetupOverlay { DataContext = vm };
            var id = _overlays.CreateOverlay(view, new OverlayOptions
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ShowBackdrop = true,
                CloseOnOutsideClick = true,
                CloseOnEscape = true
            }, "AISetup");
            view.OverlayId = id;
        });
    }
}
