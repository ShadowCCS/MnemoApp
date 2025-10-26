using System;
using System.Collections.Generic;

namespace MnemoApp.Core.Extensions.Models
{
    /// <summary>
    /// State of a loaded extension
    /// </summary>
    public enum ExtensionState
    {
        Unloaded,
        Loading,
        Loaded,
        Enabled,
        Disabled,
        Failed,
        Unloading,
        Uninstalled
    }
    
    /// <summary>
    /// Trust level for an extension
    /// </summary>
    public enum ExtensionTrustLevel
    {
        /// <summary>
        /// User has not made a trust decision
        /// </summary>
        Untrusted,
        
        /// <summary>
        /// User trusts this extension
        /// </summary>
        Trusted,
        
        /// <summary>
        /// Extension is in local development folder
        /// </summary>
        Development,
        
        /// <summary>
        /// Extension is signed and verified (future)
        /// </summary>
        Verified
    }

    /// <summary>
    /// Runtime metadata for an extension
    /// </summary>
    public class ExtensionMetadata
    {
        /// <summary>
        /// Extension manifest
        /// </summary>
        public required ExtensionManifest Manifest { get; set; }
        
        /// <summary>
        /// Current state
        /// </summary>
        public ExtensionState State { get; set; } = ExtensionState.Unloaded;
        
        /// <summary>
        /// Trust level
        /// </summary>
        public ExtensionTrustLevel TrustLevel { get; set; } = ExtensionTrustLevel.Untrusted;
        
        /// <summary>
        /// Granted permissions
        /// </summary>
        public ExtensionPermission GrantedPermissions { get; set; } = ExtensionPermission.None;
        
        /// <summary>
        /// Installation directory path
        /// </summary>
        public required string InstallPath { get; set; }
        
        /// <summary>
        /// Load mode (compiled vs source)
        /// </summary>
        public ExtensionLoadMode LoadMode { get; set; }
        
        /// <summary>
        /// When extension was installed
        /// </summary>
        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// When extension was last loaded
        /// </summary>
        public DateTime? LastLoadedAt { get; set; }
        
        /// <summary>
        /// Load errors (if any)
        /// </summary>
        public List<string> LoadErrors { get; set; } = new();
        
        /// <summary>
        /// Whether extension is enabled (persisted setting)
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Assembly name (for compiled extensions)
        /// </summary>
        public string? AssemblyName { get; set; }
        
        /// <summary>
        /// Main class type name
        /// </summary>
        public string? MainTypeName { get; set; }
    }
}

