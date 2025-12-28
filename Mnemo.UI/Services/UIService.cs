using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class UIService : IUIService
{
    public IOverlayService Overlays { get; }
    public ILoadingService Loading { get; }
    public IThemeService Themes { get; }

    public UIService(
        IOverlayService overlays, 
        ILoadingService loading, 
        IThemeService themes)
    {
        Overlays = overlays;
        Loading = loading;
        Themes = themes;
    }
}

