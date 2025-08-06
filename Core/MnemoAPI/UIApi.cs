using MnemoApp.Core.Services;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// UI-related API endpoints
    /// </summary>
    public class UIApi
    {
        public ThemeApi themes { get; }

        public UIApi(IThemeService themeService)
        {
            themes = new ThemeApi(themeService);
        }
    }

    /// <summary>
    /// Theme-related API endpoints
    /// </summary>
    public class ThemeApi
    {
        private readonly IThemeService _themeService;

        public ThemeApi(IThemeService themeService)
        {
            _themeService = themeService;
        }

        /// <summary>
        /// Gets all available themes (core + custom)
        /// </summary>
        public async System.Threading.Tasks.Task<System.Collections.Generic.List<ThemeManifest>> getAllThemes()
        {
            return await _themeService.GetAllThemesAsync();
        }

        /// <summary>
        /// Gets a specific theme by name
        /// </summary>
        public async System.Threading.Tasks.Task<ThemeManifest?> getTheme(string name)
        {
            return await _themeService.GetThemeAsync(name);
        }

        /// <summary>
        /// Sets the current theme
        /// </summary>
        public async System.Threading.Tasks.Task<bool> setTheme(string name)
        {
            return await _themeService.SetThemeAsync(name);
        }

        /// <summary>
        /// Gets the currently active theme
        /// </summary>
        public ThemeManifest? getCurrentTheme()
        {
            return _themeService.GetCurrentTheme();
        }
    }
}