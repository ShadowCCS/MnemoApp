using System;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.MnemoAPI;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Context provided to extensions for accessing host functionality
    /// </summary>
    public interface IExtensionContext
    {
        /// <summary>
        /// Extension metadata
        /// </summary>
        ExtensionMetadata Metadata { get; }
        
        /// <summary>
        /// Access to MnemoAPI (permission-wrapped)
        /// </summary>
        IMnemoAPI API { get; }
        
        /// <summary>
        /// Service provider for dependency injection
        /// </summary>
        IServiceProvider Services { get; }
        
        /// <summary>
        /// Logger for the extension
        /// </summary>
        IExtensionLogger Logger { get; }
        
        /// <summary>
        /// Helper for registering API endpoints
        /// </summary>
        ExtensionApiHelper ApiHelper { get; }
        
        /// <summary>
        /// Extension's data directory (for storing files)
        /// </summary>
        string DataDirectory { get; }
        
        /// <summary>
        /// Extension's storage key prefix (for runtime storage)
        /// </summary>
        string StoragePrefix { get; }
        
        /// <summary>
        /// Check if extension has a specific permission
        /// </summary>
        bool HasPermission(ExtensionPermission permission);
        
        /// <summary>
        /// Request additional permissions at runtime
        /// </summary>
        Task<bool> RequestPermissionAsync(ExtensionPermission permission);
    }
    
    /// <summary>
    /// Logger interface for extensions
    /// </summary>
    public interface IExtensionLogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogDebug(string message);
    }
}

