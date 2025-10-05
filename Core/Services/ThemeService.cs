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
using MnemoApp.Data.Runtime;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Service for managing application themes
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly MnemoDataApi _dataApi;
        private List<ThemeManifest> _cachedThemes = new();
        private ThemeManifest? _currentTheme;
        private const string CoreThemeBasePath = "avares://MnemoApp/UI/Themes/Core/";
        private StyleInclude? _appliedThemeStyle;
        private readonly SemaphoreSlim _themeCacheLock = new(1, 1);
        private Task? _cacheWarmupTask;


        public ThemeService(MnemoDataApi dataApi)
        {
            _dataApi = dataApi;
        }

        public async Task<List<ThemeManifest>> GetAllThemesAsync()
        {
            if (_cachedThemes.Count == 0)
            {
                _cacheWarmupTask ??= RefreshThemeCacheAsync();
                await _cacheWarmupTask;
            }
            return _cachedThemes.ToList();
        }

        public async Task<ThemeManifest?> GetThemeAsync(string name)
        {
            var themes = await GetAllThemesAsync();
            return themes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public Task<bool> SetThemeAsync(string name) => ApplyThemeAsync(name);

        public ThemeManifest? GetCurrentTheme()
        {
            return _currentTheme;
        }

        public async Task RefreshThemeCacheAsync()
        {
            await _themeCacheLock.WaitAsync();
            try
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
            finally
            {
                _themeCacheLock.Release();
            }
        }

        public async Task LoadThemeFromSettingsAsync()
        {
            try
            {
                var unified = TryGetUnifiedTheme();
                var selected = !string.IsNullOrWhiteSpace(unified)
                    ? unified
                    : _dataApi.GetProperty<string>("Theme");

                await ApplyThemeAsync(string.IsNullOrWhiteSpace(selected) ? "Dawn" : selected!);
            }
            catch
            {
                await ApplyThemeAsync("Dawn");
            }
        }

        private async Task ScanCoreThemesAsync(ThemeScanResult result)
        {
            try
            {
                // Known core themes (add any new embedded core themes here)
                var coreThemeNames = new[] { "Dawn", "Dusk", "Lumina"};
                
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
                if (app == null) return;

                try
                {
                    if (_appliedThemeStyle != null)
                    {
                        app.Styles.Remove(_appliedThemeStyle);
                        _appliedThemeStyle = null;
                    }

                    var themeStyle = new StyleInclude(new Uri("avares://MnemoApp/"))
                    {
                        Source = new Uri(theme.ThemePath)
                    };
                    app.Styles.Add(themeStyle);
                    _appliedThemeStyle = themeStyle;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error applying theme on UI thread: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw;
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


        private Task SaveThemeToSettingsAsync(string themeName)
        {
            try
            {
                _dataApi.SetProperty("Theme", themeName);
                return Task.CompletedTask;
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

            if (_currentTheme != null &&
                string.Equals(_currentTheme.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var theme = await GetThemeAsync(name);
            if (theme == null)
            {
                return false;
            }
            await ApplyThemeInternalAsync(theme);
            _currentTheme = theme;
            await UpdateLastUsedAsync(theme.Name);
            await SaveThemeToSettingsAsync(theme.Name);
            TrySetUnifiedTheme(theme.Name);
            return true;
        }

        private string? TryGetUnifiedTheme()
        {
            try
            {
                return _dataApi.GetProperty<string>("Theme", StorageScope.Runtime);
            }
            catch
            {
                return null;
            }
        }

        private void TrySetUnifiedTheme(string value)
        {
            try
            {
                _dataApi.SetProperty("Theme", value, StorageScope.Runtime);
            }
            catch
            {
                // ignore
            }
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