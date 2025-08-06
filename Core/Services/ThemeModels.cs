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
        
        /// <summary>
        /// Path to the theme.axaml file - can be avares:// URI or file path
        /// </summary>
        public required string ThemePath { get; set; }
        
        /// <summary>
        /// Whether this is a core theme (embedded) or custom theme (on disk)
        /// </summary>
        public required bool IsCore { get; set; }
        
        /// <summary>
        /// Full directory path where the theme is located
        /// </summary>
        public required string DirectoryPath { get; set; }
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