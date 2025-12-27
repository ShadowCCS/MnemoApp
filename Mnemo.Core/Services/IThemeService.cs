using System.Collections.Generic;
using System.Threading.Tasks;

using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IThemeService
{
    Task ApplyThemeAsync(string themeName);
    Task<IEnumerable<ThemeManifest>> GetAllThemesAsync();
    string GetCurrentTheme();
    void StartWatching();
    void StopWatching();
    Task<bool> ImportAsync(string path);
    Task ExportAsync(string themeName, string path);
}

