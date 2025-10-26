using System;

namespace MnemoApp.Core.Extensions.Models
{
    /// <summary>
    /// Permissions that extensions can request
    /// </summary>
    [Flags]
    public enum ExtensionPermission
    {
        None = 0,
        
        /// <summary>
        /// Read/write access to files outside extension directory
        /// </summary>
        FileAccess = 1 << 0,
        
        /// <summary>
        /// Network access (HTTP requests, etc.)
        /// </summary>
        NetworkAccess = 1 << 1,
        
        /// <summary>
        /// Can register UI views, sidebar items, overlays
        /// </summary>
        UIAccess = 1 << 2,
        
        /// <summary>
        /// Can register custom API endpoints
        /// </summary>
        ApiRegistration = 1 << 3,
        
        /// <summary>
        /// Full unrestricted access to all APIs
        /// </summary>
        FullTrust = 1 << 4,
        
        /// <summary>
        /// All permissions (for convenience)
        /// </summary>
        All = FileAccess | NetworkAccess | UIAccess | ApiRegistration | FullTrust
    }
}

