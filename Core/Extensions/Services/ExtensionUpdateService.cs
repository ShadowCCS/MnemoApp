using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Extensions.Packaging;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Handles extension updates from various sources
    /// </summary>
    public class ExtensionUpdateService
    {
        private readonly IExtensionService _extensionService;
        private readonly HttpClient _httpClient;
        private readonly ExtensionUnpacker _unpacker;
        private readonly string _updateCachePath;

        public ExtensionUpdateService(IExtensionService extensionService)
        {
            _extensionService = extensionService;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _unpacker = new ExtensionUnpacker();
            
            _updateCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MnemoApp",
                "ExtensionUpdates"
            );
            
            Directory.CreateDirectory(_updateCachePath);
        }

        /// <summary>
        /// Check for updates for all installed extensions
        /// </summary>
        public async Task<List<ExtensionUpdate>> CheckForUpdatesAsync()
        {
            var updates = new List<ExtensionUpdate>();
            var extensions = _extensionService.GetAllExtensions();

            foreach (var ext in extensions)
            {
                // Skip bundled/development extensions
                if (ext.TrustLevel == ExtensionTrustLevel.Development)
                {
                    continue;
                }

                var updateInfo = await CheckExtensionUpdateAsync(ext);
                if (updateInfo != null && updateInfo.IsUpdateAvailable)
                {
                    updates.Add(updateInfo);
                }
            }

            return updates;
        }

        /// <summary>
        /// Check for update for a specific extension
        /// </summary>
        public async Task<ExtensionUpdate?> CheckExtensionUpdateAsync(ExtensionMetadata extension)
        {
            try
            {
                // Check if extension manifest specifies an update URL
                var updateUrl = extension.Manifest.Website;
                if (string.IsNullOrEmpty(updateUrl))
                {
                    return null;
                }

                // Try to fetch update manifest
                // Format: {website}/updates/manifest.json
                var updateManifestUrl = GetUpdateManifestUrl(updateUrl);
                
                var response = await _httpClient.GetStringAsync(updateManifestUrl);
                var updateManifest = JsonSerializer.Deserialize<UpdateManifest>(response);

                if (updateManifest == null)
                {
                    return null;
                }

                // Compare versions
                if (!Version.TryParse(extension.Manifest.Version, out var currentVersion) ||
                    !Version.TryParse(updateManifest.Version, out var latestVersion))
                {
                    return null;
                }

                if (latestVersion > currentVersion)
                {
                    return new ExtensionUpdate
                    {
                        ExtensionName = extension.Manifest.Name,
                        CurrentVersion = extension.Manifest.Version,
                        LatestVersion = updateManifest.Version,
                        DownloadUrl = updateManifest.DownloadUrl,
                        Changelog = updateManifest.Changelog,
                        IsUpdateAvailable = true,
                        ReleaseDate = updateManifest.ReleaseDate
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Failed to check update for {extension.Manifest.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download and install an extension update
        /// </summary>
        public async Task<(bool success, string? error)> UpdateExtensionAsync(string extensionName)
        {
            var extension = _extensionService.GetExtension(extensionName);
            if (extension == null)
            {
                return (false, "Extension not found");
            }

            var updateInfo = await CheckExtensionUpdateAsync(extension);
            if (updateInfo == null || !updateInfo.IsUpdateAvailable)
            {
                return (false, "No update available");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Downloading update for {extensionName}...");

                // Download the update package
                var packagePath = Path.Combine(_updateCachePath, $"{extensionName}_{updateInfo.LatestVersion}.mnemoext");
                
                using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl))
                {
                    response.EnsureSuccessStatusCode();
                    
                    await using var fileStream = File.Create(packagePath);
                    await response.Content.CopyToAsync(fileStream);
                }

                System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Downloaded to {packagePath}");

                // Uninstall current version
                var uninstalled = await _extensionService.UninstallExtensionAsync(extensionName);
                if (!uninstalled)
                {
                    return (false, "Failed to uninstall current version");
                }

                // Install new version
                var (installed, installError) = await _extensionService.InstallExtensionAsync(packagePath);
                if (!installed)
                {
                    return (false, installError ?? "Failed to install update");
                }

                // Clean up downloaded package
                try
                {
                    File.Delete(packagePath);
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Successfully updated {extensionName} to {updateInfo.LatestVersion}");
                return (true, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Update failed: {ex.Message}");
                return (false, $"Update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Enable auto-updates for an extension
        /// </summary>
        public void SetAutoUpdate(string extensionName, bool enabled)
        {
            // This would be persisted to settings storage
            // For now, just log
            System.Diagnostics.Debug.WriteLine($"[UPDATE_SERVICE] Auto-update for {extensionName}: {enabled}");
        }

        private string GetUpdateManifestUrl(string websiteUrl)
        {
            // Normalize URL
            if (!websiteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !websiteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                websiteUrl = $"https://{websiteUrl}";
            }

            // Append update path
            return $"{websiteUrl.TrimEnd('/')}/updates/manifest.json";
        }
    }

    /// <summary>
    /// Update manifest from remote source
    /// </summary>
    internal class UpdateManifest
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Changelog { get; set; }
        public DateTime ReleaseDate { get; set; }
    }

    /// <summary>
    /// Information about an available extension update
    /// </summary>
    public class ExtensionUpdate
    {
        public string ExtensionName { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Changelog { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public DateTime ReleaseDate { get; set; }
    }
}

