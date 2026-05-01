using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private const string ThemeSettingKey = "Appearance.Theme";
    private const string DefaultTheme = "Dawn";

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task ApplyThemeAsync(string themeName) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var app = Application.Current;
            if (app == null) return;

            var themeUri = new Uri($"avares://Mnemo.UI/Themes/Core/{themeName}/theme.axaml");

            // Find existing theme style and replace it
            var existingTheme = app.Styles.FirstOrDefault(s => s is StyleInclude si && si.Source?.ToString().Contains("/Themes/Core/") == true);

            if (existingTheme != null)
            {
                var index = app.Styles.IndexOf(existingTheme);
                app.Styles[index] = new StyleInclude(new Uri("avares://Mnemo.UI/"))
                {
                    Source = themeUri
                };
            }
            else
            {
                app.Styles.Add(new StyleInclude(new Uri("avares://Mnemo.UI/"))
                {
                    Source = themeUri
                });
            }

            await _settingsService.SetAsync(ThemeSettingKey, themeName).ConfigureAwait(false);
        });

    public Task<IEnumerable<ThemeManifest>> GetAllThemesAsync()
    {
        // Bundled themes baked into the app package.
        var themes = new List<ThemeManifest>
        {
            new ThemeManifest 
            { 
                Name = "Dawn", 
                DisplayName = "Dawn", 
                Description = "Default light theme",
                PreviewColors = new List<string> { "#F5F5F7", "#FFFFFF", "#E5E5E7" }
            },
            new ThemeManifest 
            { 
                Name = "Noon", 
                DisplayName = "Noon", 
                Description = "Warm editorial light theme",
                PreviewColors = new List<string> { "#FBF8F0", "#F5EFE3", "#EADFCB" }
            },
            new ThemeManifest 
            { 
                Name = "Dusk", 
                DisplayName = "Dusk", 
                Description = "Default dark theme",
                PreviewColors = new List<string> { "#1A1A1C", "#2A2A2C", "#3A3A3C" }
            }
        };

        return Task.FromResult<IEnumerable<ThemeManifest>>(themes);
    }

    public async Task<string> GetCurrentThemeAsync()
    {
        return await _settingsService.GetAsync(ThemeSettingKey, DefaultTheme);
    }

    public void StartWatching() { }
    public void StopWatching() { }

    public Task<bool> ImportAsync(string path) => Task.FromResult(false);
    public Task ExportAsync(string themeName, string path) => Task.CompletedTask;
}

