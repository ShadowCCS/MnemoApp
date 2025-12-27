namespace Mnemo.Core.Services;

public interface IUIService
{
    IOverlayService Overlays { get; }
    IToastService Toasts { get; }
    ILoadingService Loading { get; }
    IThemeService Themes { get; }
}

