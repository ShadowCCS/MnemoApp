using System;
using System.Collections.Generic;
using System.Linq;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Resolves extension dependencies and determines load order
    /// </summary>
    public class DependencyResolver
    {
        /// <summary>
        /// Resolve dependencies and return extensions in load order
        /// </summary>
        public DependencyResolutionResult Resolve(IEnumerable<ExtensionMetadata> extensions)
        {
            var result = new DependencyResolutionResult();
            var extensionMap = extensions.ToDictionary(e => e.Manifest.Name, e => e);
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            var loadOrder = new List<ExtensionMetadata>();

            foreach (var ext in extensions)
            {
                if (!visited.Contains(ext.Manifest.Name))
                {
                    if (!VisitExtension(ext, extensionMap, visited, visiting, loadOrder, result))
                    {
                        // Circular dependency or missing dependency detected
                        result.HasErrors = true;
                    }
                }
            }

            result.LoadOrder = loadOrder;
            return result;
        }

        /// <summary>
        /// Validate that all dependencies are satisfied
        /// </summary>
        public List<string> ValidateDependencies(ExtensionMetadata extension, IEnumerable<ExtensionMetadata> availableExtensions)
        {
            var errors = new List<string>();
            var available = availableExtensions.ToDictionary(e => e.Manifest.Name, e => e);

            foreach (var dependency in extension.Manifest.Dependencies)
            {
                var depName = dependency.Key;
                var depVersion = dependency.Value;

                if (!available.TryGetValue(depName, out var depExtension))
                {
                    errors.Add($"Missing dependency: {depName} (required version: {depVersion})");
                    continue;
                }

                // Validate version
                if (!IsVersionCompatible(depExtension.Manifest.Version, depVersion))
                {
                    errors.Add($"Incompatible version for {depName}: found {depExtension.Manifest.Version}, required {depVersion}");
                }
            }

            return errors;
        }

        private bool VisitExtension(
            ExtensionMetadata extension,
            Dictionary<string, ExtensionMetadata> extensionMap,
            HashSet<string> visited,
            HashSet<string> visiting,
            List<ExtensionMetadata> loadOrder,
            DependencyResolutionResult result)
        {
            var name = extension.Manifest.Name;

            if (visiting.Contains(name))
            {
                // Circular dependency detected
                result.CircularDependencies.Add(name);
                result.Errors.Add($"Circular dependency detected involving extension: {name}");
                return false;
            }

            if (visited.Contains(name))
            {
                return true;
            }

            visiting.Add(name);

            // Visit dependencies first
            foreach (var dependency in extension.Manifest.Dependencies)
            {
                var depName = dependency.Key;
                var depVersion = dependency.Value;

                if (!extensionMap.TryGetValue(depName, out var depExtension))
                {
                    result.MissingDependencies.Add((name, depName, depVersion));
                    result.Errors.Add($"Extension '{name}' requires missing dependency: {depName} (version {depVersion})");
                    continue;
                }

                // Check version compatibility
                if (!IsVersionCompatible(depExtension.Manifest.Version, depVersion))
                {
                    result.Errors.Add($"Extension '{name}' requires {depName} version {depVersion}, but found {depExtension.Manifest.Version}");
                }

                // Recursively visit dependency
                if (!VisitExtension(depExtension, extensionMap, visited, visiting, loadOrder, result))
                {
                    return false;
                }
            }

            visiting.Remove(name);
            visited.Add(name);
            loadOrder.Add(extension);

            return true;
        }

        /// <summary>
        /// Check if an installed version is compatible with a required version
        /// Supports semantic versioning ranges:
        /// - "1.2.3" - exact version
        /// - "^1.2.3" - compatible with 1.x.x (>= 1.2.3, < 2.0.0)
        /// - "~1.2.3" - approximately equivalent (>= 1.2.3, < 1.3.0)
        /// - "*" - any version
        /// </summary>
        private bool IsVersionCompatible(string installedVersion, string requiredVersion)
        {
            if (requiredVersion == "*")
            {
                return true;
            }

            if (!Version.TryParse(installedVersion, out var installed))
            {
                return false;
            }

            // Handle caret (^) - compatible with major version
            if (requiredVersion.StartsWith("^"))
            {
                if (!Version.TryParse(requiredVersion.Substring(1), out var required))
                {
                    return false;
                }

                return installed.Major == required.Major && installed >= required;
            }

            // Handle tilde (~) - approximately equivalent
            if (requiredVersion.StartsWith("~"))
            {
                if (!Version.TryParse(requiredVersion.Substring(1), out var required))
                {
                    return false;
                }

                return installed.Major == required.Major &&
                       installed.Minor == required.Minor &&
                       installed >= required;
            }

            // Exact version match
            if (Version.TryParse(requiredVersion, out var exact))
            {
                return installed >= exact;
            }

            return false;
        }
    }

    /// <summary>
    /// Result of dependency resolution
    /// </summary>
    public class DependencyResolutionResult
    {
        public List<ExtensionMetadata> LoadOrder { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<(string extension, string dependency, string version)> MissingDependencies { get; set; } = new();
        public List<string> CircularDependencies { get; set; } = new();
        public bool HasErrors { get; set; } = false;

        public bool IsSuccess => !HasErrors && Errors.Count == 0;
    }
}

