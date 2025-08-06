using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Controls;

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
        private readonly string _customThemesPath;
        private readonly string _settingsPath;

        public ThemeService()
        {
            // Set up paths
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MnemoApp");
            _customThemesPath = Path.Combine(dataDirectory, "Themes");
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
                await ApplyThemeAsync(theme);
                System.Diagnostics.Debug.WriteLine("Theme applied successfully");
                
                // Save to settings
                await SaveThemeToSettingsAsync(name);
                System.Diagnostics.Debug.WriteLine("Theme saved to settings");
                
                _currentTheme = theme;
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
            
            // Scan custom themes
            await ScanCustomThemesAsync(result);
            
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
                // Known core themes (we could make this dynamic if needed)
                var coreThemeNames = new[] { "Dawn", "Dusk" };
                
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

        private async Task ScanCustomThemesAsync(ThemeScanResult result)
        {
            try
            {
                if (!Directory.Exists(_customThemesPath))
                {
                    return;
                }

                var themeDirectories = Directory.GetDirectories(_customThemesPath);
                
                foreach (var themeDir in themeDirectories)
                {
                    try
                    {
                        var manifestPath = Path.Combine(themeDir, "manifest.json");
                        var themePath = Path.Combine(themeDir, "theme.axaml");
                        
                        if (File.Exists(manifestPath) && File.Exists(themePath))
                        {
                            var manifestJson = await File.ReadAllTextAsync(manifestPath);
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
                                    ThemePath = themePath,
                                    IsCore = false,
                                    DirectoryPath = themeDir
                                };
                                
                                result.Themes.Add(manifest);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var themeName = Path.GetFileName(themeDir);
                        result.Errors.Add($"Error loading custom theme '{themeName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error scanning custom themes: {ex.Message}");
            }
        }

        private async Task ApplyThemeAsync(ThemeManifest theme)
        {
            await Task.Run(() =>
            {
                try
                {
                    ResourceInclude themeResource;
                    
                    if (theme.IsCore)
                    {
                        // For core themes, use avares:// URI
                        themeResource = new ResourceInclude(new Uri("avares://MnemoApp/"))
                        {
                            Source = new Uri(theme.ThemePath)
                        };
                    }
                    else
                    {
                        // For custom themes, use file path
                        themeResource = new ResourceInclude(new Uri("file:///"))
                        {
                            Source = new Uri($"file:///{theme.ThemePath.Replace('\\', '/')}")
                        };
                    }

                    // Apply on UI thread
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var app = Application.Current;
                        if (app != null)
                        {
                            // Clear existing theme resources from MergedDictionaries
                            var existingResourceThemes = app.Resources.MergedDictionaries
                                .OfType<ResourceInclude>()
                                .Where(s => s.Source?.ToString().Contains("/theme.axaml") == true)
                                .ToList();
                            
                            foreach (var existingTheme in existingResourceThemes)
                            {
                                app.Resources.MergedDictionaries.Remove(existingTheme);
                            }
                            
                            // Add new theme to MergedDictionaries
                            app.Resources.MergedDictionaries.Add(themeResource);
                        }
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to apply theme '{theme.Name}': {ex.Message}", ex);
                }
            });
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
    }
}