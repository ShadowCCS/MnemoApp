using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Extensions.Packaging
{
    /// <summary>
    /// Creates .mnemoext packages from extension directories
    /// </summary>
    public class ExtensionPackager
    {
        /// <summary>
        /// Package an extension directory into a .mnemoext file
        /// </summary>
        public async Task<(bool success, string? error)> PackageExtensionAsync(
            string extensionDirectory,
            string outputPath)
        {
            try
            {
                if (!Directory.Exists(extensionDirectory))
                {
                    return (false, "Extension directory not found");
                }

                // Validate manifest exists
                var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    return (false, "manifest.json not found in extension directory");
                }

                // Ensure output has correct extension
                if (!outputPath.EndsWith(".mnemoext", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".mnemoext";
                }

                // Delete existing package
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                // Create ZIP archive
                await Task.Run(() =>
                {
                    ZipFile.CreateFromDirectory(
                        extensionDirectory,
                        outputPath,
                        CompressionLevel.Optimal,
                        includeBaseDirectory: false
                    );
                });

                return (true, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_PACKAGER] Failed to package: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Validate an extension directory before packaging
        /// </summary>
        public (bool valid, string[] errors) ValidateExtensionDirectory(string extensionDirectory)
        {
            var errors = new System.Collections.Generic.List<string>();

            if (!Directory.Exists(extensionDirectory))
            {
                errors.Add("Directory does not exist");
                return (false, errors.ToArray());
            }

            // Check for manifest
            var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                errors.Add("manifest.json not found");
            }
            else
            {
                // Try to parse manifest
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<ExtensionManifest>(json);
                    
                    if (manifest == null)
                    {
                        errors.Add("Failed to parse manifest.json");
                    }
                    else
                    {
                        // Validate required fields
                        if (string.IsNullOrWhiteSpace(manifest.Name))
                        {
                            errors.Add("manifest.name is required");
                        }
                        if (string.IsNullOrWhiteSpace(manifest.Version))
                        {
                            errors.Add("manifest.version is required");
                        }
                        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
                        {
                            errors.Add("manifest.apiVersion is required");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Invalid manifest.json: {ex.Message}");
                }
            }

            return (errors.Count == 0, errors.ToArray());
        }
    }
}

