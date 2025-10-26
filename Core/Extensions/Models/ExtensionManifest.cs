using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MnemoApp.Core.Extensions.Models
{
    /// <summary>
    /// Extension manifest (manifest.json schema)
    /// </summary>
    public class ExtensionManifest
    {
        /// <summary>
        /// Unique identifier for the extension (e.g., "my-extension")
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Semantic version (e.g., "1.0.0")
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";
        
        /// <summary>
        /// Required MnemoApp API version (e.g., "1.0.0")
        /// </summary>
        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = "1.0.0";
        
        /// <summary>
        /// Display name for the extension
        /// </summary>
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        
        /// <summary>
        /// Extension description
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
        
        /// <summary>
        /// Author name or organization
        /// </summary>
        [JsonPropertyName("author")]
        public string? Author { get; set; }
        
        /// <summary>
        /// Website or repository URL
        /// </summary>
        [JsonPropertyName("website")]
        public string? Website { get; set; }
        
        /// <summary>
        /// License identifier (e.g., "MIT", "Apache-2.0")
        /// </summary>
        [JsonPropertyName("license")]
        public string? License { get; set; }
        
        /// <summary>
        /// Path to extension icon (relative to extension folder)
        /// </summary>
        [JsonPropertyName("icon")]
        public string? Icon { get; set; }
        
        /// <summary>
        /// Entry point assembly name (for compiled extensions)
        /// </summary>
        [JsonPropertyName("entryPoint")]
        public string? EntryPoint { get; set; }
        
        /// <summary>
        /// Main class that implements IMnemoExtension (fully qualified name)
        /// </summary>
        [JsonPropertyName("mainClass")]
        public string? MainClass { get; set; }
        
        /// <summary>
        /// Requested permissions
        /// </summary>
        [JsonPropertyName("permissions")]
        public List<string> Permissions { get; set; } = new();
        
        /// <summary>
        /// Extension dependencies (other extension names)
        /// </summary>
        [JsonPropertyName("dependencies")]
        public Dictionary<string, string> Dependencies { get; set; } = new();
        
        /// <summary>
        /// Tags for categorization
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();
        
        /// <summary>
        /// Changelog or release notes
        /// </summary>
        [JsonPropertyName("changelog")]
        public string? Changelog { get; set; }
        
        /// <summary>
        /// Load mode (auto-detected if not specified)
        /// </summary>
        [JsonPropertyName("loadMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExtensionLoadMode? LoadMode { get; set; }

        /// <summary>
        /// Parse permissions list to ExtensionPermission flags
        /// </summary>
        public ExtensionPermission GetPermissionFlags()
        {
            var flags = ExtensionPermission.None;
            
            foreach (var permission in Permissions)
            {
                if (Enum.TryParse<ExtensionPermission>(permission, true, out var parsed))
                {
                    flags |= parsed;
                }
            }
            
            return flags;
        }
    }
}

