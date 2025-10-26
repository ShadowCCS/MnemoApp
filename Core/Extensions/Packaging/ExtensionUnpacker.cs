using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Extensions.Packaging
{
    /// <summary>
    /// Extracts and validates .mnemoext packages
    /// </summary>
    public class ExtensionUnpacker
    {
        /// <summary>
        /// Extract a .mnemoext package to a directory
        /// </summary>
        public async Task<(bool success, ExtensionManifest? manifest, string? error)> UnpackExtensionAsync(
            string packagePath,
            string destinationDirectory)
        {
            try
            {
                if (!File.Exists(packagePath))
                {
                    return (false, null, "Package file not found");
                }

                // Create temp extraction directory
                var tempDir = Path.Combine(Path.GetTempPath(), $"mnemo_ext_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Extract to temp directory
                    await Task.Run(() =>
                    {
                        ZipFile.ExtractToDirectory(packagePath, tempDir);
                    });

                    // Validate manifest
                    var manifestPath = Path.Combine(tempDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        return (false, null, "Package does not contain manifest.json");
                    }

                    var manifest = await ValidateManifestAsync(manifestPath);
                    if (manifest == null)
                    {
                        return (false, null, "Invalid or corrupted manifest.json");
                    }

                    // Create destination directory
                    var finalDestination = Path.Combine(destinationDirectory, manifest.Name);
                    if (Directory.Exists(finalDestination))
                    {
                        Directory.Delete(finalDestination, true);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(finalDestination)!);

                    // Move from temp to final destination
                    Directory.Move(tempDir, finalDestination);

                    return (true, manifest, null);
                }
                finally
                {
                    // Clean up temp directory if it still exists
                    if (Directory.Exists(tempDir))
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_UNPACKER] Failed to unpack: {ex.Message}");
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Validate a .mnemoext package without extracting
        /// </summary>
        public async Task<(bool valid, ExtensionManifest? manifest, string? error)> ValidatePackageAsync(
            string packagePath)
        {
            try
            {
                if (!File.Exists(packagePath))
                {
                    return (false, null, "Package file not found");
                }

                using var archive = ZipFile.OpenRead(packagePath);
                
                // Find manifest entry
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    return (false, null, "Package does not contain manifest.json");
                }

                // Read and parse manifest
                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                {
                    return (false, null, "Failed to parse manifest.json");
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(manifest.Name))
                {
                    return (false, manifest, "manifest.name is required");
                }

                if (string.IsNullOrWhiteSpace(manifest.Version))
                {
                    return (false, manifest, "manifest.version is required");
                }

                if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
                {
                    return (false, manifest, "manifest.apiVersion is required");
                }

                return (true, manifest, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_UNPACKER] Validation failed: {ex.Message}");
                return (false, null, ex.Message);
            }
        }

        private async Task<ExtensionManifest?> ValidateManifestAsync(string manifestPath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                {
                    return null;
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(manifest.Name) ||
                    string.IsNullOrWhiteSpace(manifest.Version) ||
                    string.IsNullOrWhiteSpace(manifest.ApiVersion))
                {
                    return null;
                }

                return manifest;
            }
            catch
            {
                return null;
            }
        }
    }
}

