using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Custom AssemblyLoadContext for extension isolation
    /// </summary>
    public class ExtensionAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _extensionPath;
        private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

        public ExtensionAssemblyLoadContext(string extensionPath) : base($"Extension_{Path.GetFileName(extensionPath)}", isCollectible: true)
        {
            _extensionPath = extensionPath;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // First, try to load from the extension directory
            var assemblyPath = Path.Combine(_extensionPath, $"{assemblyName.Name}.dll");
            if (File.Exists(assemblyPath))
            {
                try
                {
                    return LoadFromAssemblyPath(assemblyPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_ALC] Failed to load {assemblyName.Name} from extension path: {ex.Message}");
                }
            }

            // If not found in extension directory, try to load from the default context
            // This allows extensions to use shared dependencies
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_ALC] Failed to load {assemblyName.Name} from default context: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load an assembly from a specific path
        /// </summary>
        public new Assembly LoadFromAssemblyPath(string assemblyPath)
        {
            if (_loadedAssemblies.TryGetValue(assemblyPath, out var cachedAssembly))
            {
                return cachedAssembly;
            }

            var assembly = base.LoadFromAssemblyPath(assemblyPath);
            _loadedAssemblies[assemblyPath] = assembly;
            return assembly;
        }

        /// <summary>
        /// Get all loaded assemblies in this context
        /// </summary>
        public IEnumerable<Assembly> GetLoadedAssemblies()
        {
            return _loadedAssemblies.Values;
        }

        /// <summary>
        /// Unload this context and all its assemblies
        /// </summary>
        public void UnloadContext()
        {
            try
            {
                // Clear the loaded assemblies dictionary
                _loadedAssemblies.Clear();
                
                // Unload the context
                Unload();
                
                System.Diagnostics.Debug.WriteLine($"[EXT_ALC] Unloaded extension context for: {_extensionPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_ALC] Error unloading context: {ex.Message}");
            }
        }
    }
}

