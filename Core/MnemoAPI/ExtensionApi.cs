using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Extensions.Services;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// API for managing extensions through MnemoAPI
    /// </summary>
    public class ExtensionApi
    {
        private readonly IExtensionService _extensionService;
        private readonly ExtensionUpdateService _updateService;

        public ExtensionApi(IExtensionService extensionService)
        {
            _extensionService = extensionService;
            _updateService = new ExtensionUpdateService(extensionService);
        }

        /// <summary>
        /// Get the API router for direct access (internal use)
        /// </summary>
        internal ExtensionApiRouter Router
        {
            get
            {
                if (_extensionService is ExtensionService service)
                {
                    return service.ApiRouter;
                }
                throw new InvalidOperationException("ExtensionService does not expose API router");
            }
        }

        /// <summary>
        /// Get all installed extensions
        /// </summary>
        public List<ExtensionInfo> listInstalled()
        {
            return _extensionService.GetAllExtensions()
                .Select(e => new ExtensionInfo
                {
                    Name = e.Manifest.Name,
                    DisplayName = e.Manifest.DisplayName ?? e.Manifest.Name,
                    Version = e.Manifest.Version,
                    Author = e.Manifest.Author,
                    Description = e.Manifest.Description,
                    IsEnabled = e.IsEnabled,
                    State = e.State.ToString(),
                    HasErrors = e.LoadErrors.Any(),
                    Permissions = e.Manifest.Permissions,
                    Icon = e.Manifest.Icon
                })
                .ToList();
        }

        /// <summary>
        /// Get detailed metadata for an extension
        /// </summary>
        public ExtensionMetadata? getMetadata(string name)
        {
            return _extensionService.GetExtension(name);
        }

        /// <summary>
        /// Install an extension from a package or folder path
        /// </summary>
        public async Task<InstallResult> install(string path)
        {
            var (success, error) = await _extensionService.InstallExtensionAsync(path);
            
            return new InstallResult
            {
                Success = success,
                Error = error
            };
        }

        /// <summary>
        /// Uninstall an extension
        /// </summary>
        public async Task<bool> uninstall(string name)
        {
            return await _extensionService.UninstallExtensionAsync(name);
        }

        /// <summary>
        /// Enable an extension
        /// </summary>
        public async Task<bool> enable(string name)
        {
            return await _extensionService.EnableExtensionAsync(name);
        }

        /// <summary>
        /// Disable an extension
        /// </summary>
        public async Task<bool> disable(string name)
        {
            return await _extensionService.DisableExtensionAsync(name);
        }

        /// <summary>
        /// Reload an extension (unload and load again)
        /// </summary>
        public async Task<bool> reload(string name)
        {
            return await _extensionService.ReloadExtensionAsync(name);
        }

        /// <summary>
        /// Refresh extension discovery
        /// </summary>
        public async Task refresh()
        {
            await _extensionService.RefreshExtensionsAsync();
        }

        /// <summary>
        /// Load an extension
        /// </summary>
        public async Task<bool> load(string name)
        {
            return await _extensionService.LoadExtensionAsync(name);
        }

        /// <summary>
        /// Unload an extension
        /// </summary>
        public async Task<bool> unload(string name)
        {
            return await _extensionService.UnloadExtensionAsync(name);
        }

        /// <summary>
        /// Call an extension API endpoint
        /// </summary>
        /// <param name="extensionName">Name of the extension</param>
        /// <param name="endpoint">API endpoint to call</param>
        /// <param name="parameters">Parameters to pass to the endpoint</param>
        public async Task<ApiCallResult> call(string extensionName, string endpoint, object? parameters = null)
        {
            return await Router.CallAsync(extensionName, endpoint, parameters);
        }

        /// <summary>
        /// Get registered API endpoints for an extension
        /// </summary>
        public List<string> getEndpoints(string extensionName)
        {
            return Router.GetEndpoints(extensionName);
        }

        /// <summary>
        /// Check for updates for all extensions
        /// </summary>
        public async Task<List<ExtensionUpdate>> checkUpdates()
        {
            return await _updateService.CheckForUpdatesAsync();
        }

        /// <summary>
        /// Check for update for a specific extension
        /// </summary>
        public async Task<ExtensionUpdate?> checkUpdate(string name)
        {
            var extension = _extensionService.GetExtension(name);
            if (extension == null)
            {
                return null;
            }

            return await _updateService.CheckExtensionUpdateAsync(extension);
        }

        /// <summary>
        /// Update an extension to the latest version
        /// </summary>
        public async Task<(bool success, string? error)> update(string name)
        {
            return await _updateService.UpdateExtensionAsync(name);
        }

        /// <summary>
        /// Set auto-update preference for an extension
        /// </summary>
        public void setAutoUpdate(string name, bool enabled)
        {
            _updateService.SetAutoUpdate(name, enabled);
        }
    }

    /// <summary>
    /// Simplified extension info for API consumers
    /// </summary>
    public class ExtensionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public string State { get; set; } = string.Empty;
        public bool HasErrors { get; set; }
        public List<string> Permissions { get; set; } = new();
        public string? Icon { get; set; }
    }

    /// <summary>
    /// Result of extension installation
    /// </summary>
    public class InstallResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}

