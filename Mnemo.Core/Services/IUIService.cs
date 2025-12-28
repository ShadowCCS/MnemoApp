namespace Mnemo.Core.Services;

public interface IUIService
{
    IOverlayService Overlays { get; }
    ILoadingService Loading { get; }
    IThemeService Themes { get; }
}

