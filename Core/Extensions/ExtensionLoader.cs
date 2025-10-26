using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Handles loading of extensions from disk
    /// </summary>
    internal class ExtensionLoader
    {
        private readonly IServiceProvider _serviceProvider;
        private const string ManifestFileName = "manifest.json";
        private const string CurrentApiVersion = "1.0.0";
        private readonly Dictionary<string, ExtensionAssemblyLoadContext> _loadContexts = new();

        public ExtensionLoader(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Discover extensions in a directory
        /// </summary>
        public List<ExtensionMetadata> DiscoverExtensions(string searchPath)
        {
            var extensions = new List<ExtensionMetadata>();
            
            if (!Directory.Exists(searchPath))
            {
                return extensions;
            }

            // Each subdirectory is potentially an extension
            var extensionDirs = Directory.GetDirectories(searchPath);
            
            foreach (var dir in extensionDirs)
            {
                try
                {
                    var manifestPath = Path.Combine(dir, ManifestFileName);
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    var manifest = LoadManifest(manifestPath);
                    if (manifest == null)
                    {
                        continue;
                    }

                    var metadata = new ExtensionMetadata
                    {
                        Manifest = manifest,
                        InstallPath = dir,
                        LoadMode = DetermineLoadMode(dir, manifest),
                        State = ExtensionState.Unloaded
                    };

                    extensions.Add(metadata);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Failed to discover extension in {dir}: {ex.Message}");
                }
            }

            return extensions;
        }

        /// <summary>
        /// Load manifest from file
        /// </summary>
        public ExtensionManifest? LoadManifest(string manifestPath)
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                return JsonSerializer.Deserialize<ExtensionManifest>(json, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Failed to load manifest {manifestPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load an extension and return the instance
        /// </summary>
        public async Task<(IMnemoExtension? instance, List<string> errors)> LoadExtensionAsync(ExtensionMetadata metadata)
        {
            var errors = new List<string>();

            try
            {
                // Validate API version
                if (!IsApiVersionCompatible(metadata.Manifest.ApiVersion))
                {
                    errors.Add($"Incompatible API version. Extension requires {metadata.Manifest.ApiVersion}, current is {CurrentApiVersion}");
                    return (null, errors);
                }

                IMnemoExtension? instance = null;

                if (metadata.LoadMode == ExtensionLoadMode.CompiledAssembly)
                {
                    instance = await LoadCompiledExtensionAsync(metadata, errors);
                }
                else if (metadata.LoadMode == ExtensionLoadMode.SourceBased)
                {
                    // For bundled extensions with source files, treat as compiled assembly
                    // since they're part of the main application
                    var bundledExtensionsPath = Path.Combine(
                        Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
                        "Modules",
                        "Extensions"
                    );
                    
                    if (metadata.InstallPath.StartsWith(bundledExtensionsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Treat as compiled assembly for bundled extensions
                        instance = await LoadCompiledExtensionAsync(metadata, errors);
                    }
                    else
                    {
                        // Note: Source-based extensions should be compiled by ExtensionService
                        // before reaching this point. If we get here, it's an error.
                        errors.Add("Source-based extensions must be compiled before loading");
                        return (null, errors);
                    }
                }

                return (instance, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load extension: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Exception loading extension {metadata.Manifest.Name}: {ex}");
                return (null, errors);
            }
        }

        /// <summary>
        /// Load a compiled DLL extension
        /// </summary>
        private Task<IMnemoExtension?> LoadCompiledExtensionAsync(ExtensionMetadata metadata, List<string> errors)
        {
            try
            {
                // Check if this is a bundled extension (part of the main application)
                var bundledExtensionsPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
                    "Modules",
                    "Extensions"
                );

                // Normalize paths for comparison
                var normalizedExtensionPath = Path.GetFullPath(metadata.InstallPath);
                var normalizedBundledPath = Path.GetFullPath(bundledExtensionsPath);
                bool isBundledExtension = normalizedExtensionPath.StartsWith(normalizedBundledPath, StringComparison.OrdinalIgnoreCase);
                
                System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Extension path: '{normalizedExtensionPath}'");
                System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Bundled path: '{normalizedBundledPath}'");
                System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Is bundled: {isBundledExtension}");

                Assembly assembly;
                if (isBundledExtension)
                {
                    // For bundled extensions, use the current application assembly
                    assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    metadata.AssemblyName = assembly.GetName().Name;
                }
                else
                {
                    // Create isolated load context for this extension
                    var loadContext = new ExtensionAssemblyLoadContext(metadata.InstallPath);
                    _loadContexts[metadata.Manifest.Name] = loadContext;

                    // Find the entry point DLL for regular extensions
                    string? dllPath = null;

                    if (!string.IsNullOrEmpty(metadata.Manifest.EntryPoint))
                    {
                        // Check if EntryPoint is an absolute path (from compilation)
                        if (Path.IsPathRooted(metadata.Manifest.EntryPoint))
                        {
                            dllPath = metadata.Manifest.EntryPoint;
                        }
                        else
                        {
                            dllPath = Path.Combine(metadata.InstallPath, metadata.Manifest.EntryPoint);
                            if (!dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                dllPath += ".dll";
                            }
                        }
                    }
                    else
                    {
                        // Look for a DLL with the extension name
                        var dllFiles = Directory.GetFiles(metadata.InstallPath, "*.dll", SearchOption.TopDirectoryOnly);
                        dllPath = dllFiles.FirstOrDefault();
                    }

                    if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                    {
                        errors.Add($"Entry point DLL not found: {dllPath ?? "no DLL specified"}");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }

                    metadata.AssemblyName = Path.GetFileNameWithoutExtension(dllPath);

                    // Load the assembly in the isolated context
                    try
                    {
                        assembly = loadContext.LoadFromAssemblyPath(dllPath);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to load assembly: {ex.Message}");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }
                }

                // Find the main class
                Type? mainType = null;

                if (!string.IsNullOrEmpty(metadata.Manifest.MainClass))
                {
                    mainType = assembly.GetType(metadata.Manifest.MainClass);
                    if (mainType == null)
                    {
                        errors.Add($"Main class not found: {metadata.Manifest.MainClass}");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }
                }
                else
                {
                    // Find first type implementing IMnemoExtension
                    var extensionTypes = assembly.GetTypes()
                        .Where(t => typeof(IMnemoExtension).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        .ToList();

                    if (extensionTypes.Count == 0)
                    {
                        errors.Add("No class implementing IMnemoExtension found");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }
                    else if (extensionTypes.Count > 1)
                    {
                        errors.Add($"Multiple classes implementing IMnemoExtension found. Please specify mainClass in manifest.");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }

                    mainType = extensionTypes[0];
                }

                metadata.MainTypeName = mainType.FullName;

                // Instantiate the extension with DI
                IMnemoExtension? instance = null;

                try
                {
                    // Try to create instance with DI
                    instance = ActivatorUtilities.CreateInstance(_serviceProvider, mainType) as IMnemoExtension;
                }
                catch
                {
                    // Fallback to parameterless constructor
                    try
                    {
                        instance = Activator.CreateInstance(mainType) as IMnemoExtension;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to instantiate extension: {ex.Message}");
                        return Task.FromResult<IMnemoExtension?>(null);
                    }
                }

                if (instance == null)
                {
                    errors.Add($"Failed to create instance of {mainType.FullName}");
                    return Task.FromResult<IMnemoExtension?>(null);
                }

                return Task.FromResult<IMnemoExtension?>(instance);
            }
            catch (Exception ex)
            {
                errors.Add($"Unexpected error loading compiled extension: {ex.Message}");
                return Task.FromResult<IMnemoExtension?>(null);
            }
        }

        /// <summary>
        /// Determine how to load an extension
        /// </summary>
        private ExtensionLoadMode DetermineLoadMode(string extensionPath, ExtensionManifest manifest)
        {
            // Use manifest override if specified
            if (manifest.LoadMode.HasValue)
            {
                return manifest.LoadMode.Value;
            }

            // Check for DLL files
            var hasDlls = Directory.GetFiles(extensionPath, "*.dll", SearchOption.TopDirectoryOnly).Any();
            if (hasDlls)
            {
                return ExtensionLoadMode.CompiledAssembly;
            }

            // Check for source files
            var hasSourceFiles = Directory.GetFiles(extensionPath, "*.cs", SearchOption.AllDirectories).Any();
            if (hasSourceFiles)
            {
                // If this is in the bundled extensions directory, treat as compiled assembly
                // since it's part of the main application
                var bundledExtensionsPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
                    "Modules",
                    "Extensions"
                );

                if (extensionPath.StartsWith(bundledExtensionsPath, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtensionLoadMode.CompiledAssembly;
                }

                return ExtensionLoadMode.SourceBased;
            }

            // Default to compiled
            return ExtensionLoadMode.CompiledAssembly;
        }

        /// <summary>
        /// Check if extension API version is compatible
        /// </summary>
        private bool IsApiVersionCompatible(string extensionApiVersion)
        {
            try
            {
                var current = Version.Parse(CurrentApiVersion);
                var required = Version.Parse(extensionApiVersion);

                // Major version must match, minor version can be equal or greater
                return current.Major == required.Major && current.Minor >= required.Minor;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Unload an extension's assembly context
        /// </summary>
        public void UnloadExtension(string extensionName)
        {
            if (_loadContexts.TryGetValue(extensionName, out var loadContext))
            {
                try
                {
                    loadContext.UnloadContext();
                    _loadContexts.Remove(extensionName);
                    System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Unloaded assembly context for extension: {extensionName}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Error unloading extension {extensionName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Unload all extension contexts
        /// </summary>
        public void UnloadAllExtensions()
        {
            foreach (var kvp in _loadContexts.ToList())
            {
                try
                {
                    kvp.Value.UnloadContext();
                    System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Unloaded assembly context for extension: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_LOADER] Error unloading extension {kvp.Key}: {ex.Message}");
                }
            }
            _loadContexts.Clear();
        }
    }
}

