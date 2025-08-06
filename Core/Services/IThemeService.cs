using System.Collections.Generic;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Service for managing application themes
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Gets all available themes (core + custom)
        /// </summary>
        Task<List<ThemeManifest>> GetAllThemesAsync();
        
        /// <summary>
        /// Gets a specific theme by name
        /// </summary>
        Task<ThemeManifest?> GetThemeAsync(string name);
        
        /// <summary>
        /// Sets the current theme and applies it
        /// </summary>
        Task<bool> SetThemeAsync(string name);
        
        /// <summary>
        /// Gets the currently active theme
        /// </summary>
        ThemeManifest? GetCurrentTheme();
        
        /// <summary>
        /// Refreshes the theme cache by rescanning directories
        /// </summary>
        Task RefreshThemeCacheAsync();
        
        /// <summary>
        /// Loads the theme from settings on app startup
        /// </summary>
        Task LoadThemeFromSettingsAsync();
    }
}