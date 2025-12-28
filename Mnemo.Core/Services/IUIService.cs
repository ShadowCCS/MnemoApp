namespace Mnemo.Core.Services;

public interface IUIService
{
    IOverlayService Overlays { get; }
    IThemeService Themes { get; }
}

