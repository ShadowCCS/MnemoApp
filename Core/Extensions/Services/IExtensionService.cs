using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Extensions;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Service for managing extensions
    /// </summary>
    public interface IExtensionService
    {
        /// <summary>
        /// Initialize the extension service and discover extensions
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Get all discovered extensions
        /// </summary>
        IReadOnlyList<ExtensionMetadata> GetAllExtensions();

        /// <summary>
        /// Get loaded extensions
        /// </summary>
        IReadOnlyList<ExtensionMetadata> GetLoadedExtensions();

        /// <summary>
        /// Get extension by name
        /// </summary>
        ExtensionMetadata? GetExtension(string name);

        /// <summary>
        /// Get loaded extension instance for API calls
        /// </summary>
        IMnemoExtension? GetExtensionInstance(string name);

        /// <summary>
        /// Get the extension context for a loaded extension
        /// </summary>
        IExtensionContext? GetExtensionContext(string extensionName);

        /// <summary>
        /// Load and enable an extension
        /// </summary>
        Task<bool> LoadExtensionAsync(string name);

        /// <summary>
        /// Unload an extension
        /// </summary>
        Task<bool> UnloadExtensionAsync(string name);

        /// <summary>
        /// Enable an extension
        /// </summary>
        Task<bool> EnableExtensionAsync(string name);

        /// <summary>
        /// Disable an extension
        /// </summary>
        Task<bool> DisableExtensionAsync(string name);

        /// <summary>
        /// Install extension from package or folder
        /// </summary>
        Task<(bool success, string? error)> InstallExtensionAsync(string sourcePath);

        /// <summary>
        /// Uninstall an extension
        /// </summary>
        Task<bool> UninstallExtensionAsync(string name);

        /// <summary>
        /// Reload an extension (unload and load again)
        /// </summary>
        Task<bool> ReloadExtensionAsync(string name);

        /// <summary>
        /// Refresh extension discovery
        /// </summary>
        Task RefreshExtensionsAsync();

        /// <summary>
        /// Event fired when extensions are loaded/unloaded
        /// </summary>
        event EventHandler<ExtensionEventArgs>? ExtensionStateChanged;
    }

    public class ExtensionEventArgs : EventArgs
    {
        public required ExtensionMetadata Metadata { get; init; }
        public ExtensionState OldState { get; init; }
        public ExtensionState NewState { get; init; }
    }
}

