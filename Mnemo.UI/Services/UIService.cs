using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class UIService : IUIService
{
    public IOverlayService Overlays { get; }
    public IThemeService Themes { get; }

    public UIService(
        IOverlayService overlays, 
        IThemeService themes)
    {
        Overlays = overlays;
        Themes = themes;
    }
}

