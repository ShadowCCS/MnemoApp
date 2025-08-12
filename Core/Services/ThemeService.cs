using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Styling;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Service for managing application themes
    /// </summary>
    public class ThemeService : IThemeService
    {
        private List<ThemeManifest> _cachedThemes = new();
        private ThemeManifest? _currentTheme;
        private const string CoreThemeBasePath = "avares://MnemoApp/UI/Themes/Core/";
        private readonly string _settingsPath;

        public ThemeService()
        {
            // Set up paths
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MnemoApp");
            _settingsPath = Path.Combine(dataDirectory, "Portable", "settings.json");

        }

        public async Task<List<ThemeManifest>> GetAllThemesAsync()
        {
            if (_cachedThemes.Count == 0)
            {
                await RefreshThemeCacheAsync();
            }
            return _cachedThemes.ToList();
        }

        public async Task<ThemeManifest?> GetThemeAsync(string name)
        {
            var themes = await GetAllThemesAsync();
            return themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> SetThemeAsync(string name)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SetThemeAsync called for: {name}");
                
                var theme = await GetThemeAsync(name);
                if (theme == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Theme '{name}' not found");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"Found theme: {theme.Name}, IsCore: {theme.IsCore}, Path: {theme.ThemePath}");

                // Load and apply the theme
                await ApplyThemeInternalAsync(theme);
                System.Diagnostics.Debug.WriteLine("Theme applied successfully");
                
                // Save to settings
                await SaveThemeToSettingsAsync(name);
                System.Diagnostics.Debug.WriteLine("Theme saved to settings");
                
                _currentTheme = theme;
                await UpdateLastUsedAsync(theme.Name);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SetThemeAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public ThemeManifest? GetCurrentTheme()
        {
            return _currentTheme;
        }

        public async Task RefreshThemeCacheAsync()
        {
            var result = new ThemeScanResult();
            
            // Scan core themes
            await ScanCoreThemesAsync(result);
            
            _cachedThemes = result.Themes;
            
            System.Diagnostics.Debug.WriteLine($"Loaded {_cachedThemes.Count} themes:");
            foreach (var theme in _cachedThemes)
            {
                System.Diagnostics.Debug.WriteLine($"  - {theme.Name} (IsCore: {theme.IsCore})");
            }
            
            if (result.Errors.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine("Theme loading errors:");
                foreach (var error in result.Errors)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {error}");
                }
            }
        }

        public async Task LoadThemeFromSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var settingsJson = await File.ReadAllTextAsync(_settingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson);
                    
                    if (settings?.TryGetValue("Theme", out var themeValue) == true && 
                        themeValue is JsonElement element && 
                        element.ValueKind == JsonValueKind.String)
                    {
                        var themeName = element.GetString();
                        if (!string.IsNullOrEmpty(themeName))
                        {
                            await SetThemeAsync(themeName);
                            return;
                        }
                    }
                }
                
                // Fallback to Dawn theme if no setting found
                await SetThemeAsync("Dawn");
            }
            catch
            {
                // Fallback to Dawn theme on any error
                await SetThemeAsync("Dawn");
            }
        }

        private async Task ScanCoreThemesAsync(ThemeScanResult result)
        {
            try
            {
                // Known core themes (add any new embedded core themes here)
                var coreThemeNames = new[] { "Dawn", "Dusk"};
                
                foreach (var themeName in coreThemeNames)
                {
                    try
                    {
                        var manifestUri = new Uri($"{CoreThemeBasePath}{themeName}/manifest.json");
                        using var manifestStream = AssetLoader.Open(manifestUri);
                        using var reader = new StreamReader(manifestStream);
                        var manifestJson = await reader.ReadToEndAsync();
                        var manifestData = JsonSerializer.Deserialize<ThemeManifestJson>(manifestJson);
                        
                        if (manifestData != null)
                        {
                            var manifest = new ThemeManifest
                            {
                                Name = manifestData.Name,
                                Author = manifestData.Author,
                                Version = manifestData.Version,
                                Description = manifestData.Description,
                                PreviewColors = manifestData.PreviewColors,
                                Id = manifestData.Id,
                                LastUsed = manifestData.LastUsed,
                                ThemePath = $"{CoreThemeBasePath}{themeName}/theme.axaml",
                                IsCore = true,
                                DirectoryPath = $"{CoreThemeBasePath}{themeName}/"
                            };
                            
                            result.Themes.Add(manifest);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Error loading core theme '{themeName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error scanning core themes: {ex.Message}");
            }
        }

        

        // Renamed to avoid confusion with the public interface method signature
        private async Task ApplyThemeInternalAsync(ThemeManifest theme)
        {

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var app = Application.Current;
                if (app != null)
                {
                    try
                    {
                        // Remove all theme styles
                        var existingThemeStyles = app.Styles
                            .OfType<StyleInclude>()
                            .Where(s => s.Source?.ToString().Contains("/theme.axaml") == true)
                            .ToList();

                        foreach (var existingTheme in existingThemeStyles)
                        {
                            app.Styles.Remove(existingTheme);
                        }

                        

                        // Force garbage collection to clear any cached resources
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        // Create and add new theme style (core only)
                        var themeStyle = new StyleInclude(new Uri("avares://MnemoApp/"))
                        {
                            Source = new Uri(theme.ThemePath)
                        };
                        app.Styles.Add(themeStyle);

                        // Optionally, force resource reload
                        app.Resources = new Avalonia.Controls.ResourceDictionary();

                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error applying theme on UI thread: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        throw;
                    }
                }
            });
        }

        private Task UpdateLastUsedAsync(string themeName)
        {
            try
            {
                var theme = _cachedThemes.FirstOrDefault(t => t.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase));
                if (theme != null)
                {
                    theme.LastUsed = DateTimeOffset.UtcNow;
                }
            }
            catch
            {
                // Intentionally ignore; best-effort cache update only
            }
            return Task.CompletedTask;
        }


        private async Task SaveThemeToSettingsAsync(string themeName)
        {
            try
            {
                // Ensure directory exists
                var settingsDir = Path.GetDirectoryName(_settingsPath);
                if (settingsDir != null && !Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                Dictionary<string, object> settings;
                
                // Load existing settings if they exist
                if (File.Exists(_settingsPath))
                {
                    var existingJson = await File.ReadAllTextAsync(_settingsPath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new();
                }
                else
                {
                    settings = new Dictionary<string, object>();
                }

                // Update theme setting
                settings["Theme"] = themeName;

                // Save back to file
                var updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_settingsPath, updatedJson);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save theme setting: {ex.Message}", ex);
            }
        }

        // -----------------------------
        // IThemeService additions
        // -----------------------------

        public async Task<bool> ApplyThemeAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var theme = await GetThemeAsync(name);
            if (theme == null)
            {
                return false;
            }
            await ApplyThemeInternalAsync(theme);
            _currentTheme = theme;
            await UpdateLastUsedAsync(theme.Name);
            await SaveThemeToSettingsAsync(theme.Name);
            return true;
        }

        public Task<ThemeManifest> ImportThemeAsync(string sourceDirectoryPath, CancellationToken cancellationToken = default)
        {
            // Not implemented yet – keeping explicit to compile and be clear at runtime
            throw new NotSupportedException("ImportThemeAsync is not implemented in this build.");
        }

        public Task ExportThemeAsync(string themeName, string destinationDirectoryPath, CancellationToken cancellationToken = default)
        {
            // Not implemented yet – keeping explicit to compile and be clear at runtime
            throw new NotSupportedException("ExportThemeAsync is not implemented in this build.");
        }

        public void StartWatching()
        {
            // Placeholder: no-op watcher for now
        }

        public void StopWatching()
        {
            // Placeholder: no-op watcher for now
        }
    }
}