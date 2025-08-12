using System;
using System.Collections.Generic;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Represents a theme manifest containing metadata about a theme
    /// </summary>
    public class ThemeManifest
    {
        public required string Name { get; set; }
        public required string Author { get; set; }
        public required string Version { get; set; }
        public string? Description { get; set; }
        public required List<string> PreviewColors { get; set; }

        // Optional id for extensibility/versioning
        public string? Id { get; set; }

        // Last-used timestamp; for core themes this may be populated from a sidecar map
        public System.DateTimeOffset? LastUsed { get; set; }
        
        /// <summary>
        /// Path to the theme.axaml file - can be avares:// URI or file path.
        /// For packaged custom themes, this points to the extracted cache path when available.
        /// </summary>
        public required string ThemePath { get; set; }
        
        /// <summary>
        /// Whether this is a core theme (embedded) or custom theme (on disk)
        /// </summary>
        public required bool IsCore { get; set; }
        
        /// <summary>
        /// Full directory path where the theme is located. For core themes this is an avares:// directory-like URI.
        /// </summary>
        public required string DirectoryPath { get; set; }

        /// <summary>
        /// Path to the packaged content (for custom themes): content.zip
        /// </summary>
        public string? ContentZipPath { get; set; }

        /// <summary>
        /// Path to the extracted cache directory containing theme.axaml and assets (for custom themes).
        /// </summary>
        public string? ExtractedPath { get; set; }
    }

    /// <summary>
    /// DTO for theme manifest JSON files
    /// </summary>
    public class ThemeManifestJson
    {
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Id { get; set; } // Optional, as mentioned in requirements
        public string? Description { get; set; }
        public List<string> PreviewColors { get; set; } = new();
        public System.DateTimeOffset? LastUsed { get; set; }
    }

    /// <summary>
    /// Result of theme scanning operations
    /// </summary>
    public class ThemeScanResult
    {
        public List<ThemeManifest> Themes { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}