using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Routes API calls to extension handlers
    /// </summary>
    public class ExtensionApiRouter
    {
        private readonly ConcurrentDictionary<string, RegisteredApiHandler> _handlers = new();
        private readonly IExtensionService _extensionService;

        public ExtensionApiRouter(IExtensionService extensionService)
        {
            _extensionService = extensionService;
        }

        /// <summary>
        /// Register an API endpoint for an extension
        /// </summary>
        public void RegisterEndpoint(string extensionName, string endpoint, ExtensionPermission requiredPermissions)
        {
            var key = GetKey(extensionName, endpoint);
            
            _handlers[key] = new RegisteredApiHandler
            {
                ExtensionName = extensionName,
                Endpoint = endpoint,
                RequiredPermissions = requiredPermissions,
                RegisteredAt = DateTime.UtcNow
            };

            System.Diagnostics.Debug.WriteLine($"[API_ROUTER] Registered endpoint: {extensionName}.{endpoint}");
        }

        /// <summary>
        /// Unregister all endpoints for an extension
        /// </summary>
        public void UnregisterExtension(string extensionName)
        {
            var keysToRemove = new System.Collections.Generic.List<string>();
            
            foreach (var key in _handlers.Keys)
            {
                if (key.StartsWith($"{extensionName}:", StringComparison.Ordinal))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _handlers.TryRemove(key, out _);
            }

            System.Diagnostics.Debug.WriteLine($"[API_ROUTER] Unregistered {keysToRemove.Count} endpoints for {extensionName}");
        }

        /// <summary>
        /// Call an extension API endpoint
        /// </summary>
        public async Task<ApiCallResult> CallAsync(string extensionName, string endpoint, object? parameters = null)
        {
            var key = GetKey(extensionName, endpoint);

            if (!_handlers.TryGetValue(key, out var handler))
            {
                return ApiCallResult.Error($"Endpoint '{endpoint}' not found for extension '{extensionName}'");
            }

            var extension = _extensionService.GetExtension(extensionName);
            if (extension == null)
            {
                return ApiCallResult.Error($"Extension '{extensionName}' not found");
            }

            if (extension.State != ExtensionState.Enabled)
            {
                return ApiCallResult.Error($"Extension '{extensionName}' is not enabled (state: {extension.State})");
            }

            // Check permissions
            if (!extension.GrantedPermissions.HasFlag(handler.RequiredPermissions) && 
                !extension.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
            {
                return ApiCallResult.Error($"Extension lacks required permissions: {handler.RequiredPermissions}");
            }

            try
            {
                // Get the loaded extension instance
                var loadedExtensions = _extensionService.GetLoadedExtensions();
                var loadedExt = loadedExtensions.FirstOrDefault(e => e.Manifest.Name == extensionName);

                if (loadedExt == null)
                {
                    return ApiCallResult.Error($"Extension '{extensionName}' is not loaded");
                }

                // Find the extension instance and call it
                // This requires access to the actual instance, which we'll need to expose
                var result = await CallExtensionHandlerAsync(extensionName, endpoint, parameters);
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API_ROUTER] Error calling {extensionName}.{endpoint}: {ex.Message}");
                return ApiCallResult.Error($"API call failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all registered endpoints for an extension
        /// </summary>
        public System.Collections.Generic.List<string> GetEndpoints(string extensionName)
        {
            var endpoints = new System.Collections.Generic.List<string>();

            foreach (var handler in _handlers.Values)
            {
                if (handler.ExtensionName == extensionName)
                {
                    endpoints.Add(handler.Endpoint);
                }
            }

            return endpoints;
        }

        /// <summary>
        /// Check if an endpoint is registered
        /// </summary>
        public bool IsEndpointRegistered(string extensionName, string endpoint)
        {
            var key = GetKey(extensionName, endpoint);
            return _handlers.ContainsKey(key);
        }

        private string GetKey(string extensionName, string endpoint)
        {
            return $"{extensionName}:{endpoint}";
        }

        private async Task<ApiCallResult> CallExtensionHandlerAsync(string extensionName, string endpoint, object? parameters)
        {
            var instance = _extensionService.GetExtensionInstance(extensionName);
            
            if (instance == null)
            {
                return ApiCallResult.Error($"Extension '{extensionName}' instance not found");
            }

            if (instance is not IApiContributor apiContributor)
            {
                return ApiCallResult.Error($"Extension '{extensionName}' does not implement IApiContributor");
            }

            try
            {
                var result = await apiContributor.HandleApiCallAsync(endpoint, parameters);
                return ApiCallResult.Ok(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API_ROUTER] Handler exception for {extensionName}.{endpoint}: {ex.Message}");
                return ApiCallResult.Error($"Handler threw exception: {ex.Message}");
            }
        }

        private class RegisteredApiHandler
        {
            public required string ExtensionName { get; init; }
            public required string Endpoint { get; init; }
            public required ExtensionPermission RequiredPermissions { get; init; }
            public DateTime RegisteredAt { get; init; }
        }
    }

    /// <summary>
    /// Result of an API call to an extension
    /// </summary>
    public class ApiCallResult
    {
        public bool Success { get; init; }
        public object? Data { get; init; }
        public string? ErrorMessage { get; init; }

        public static ApiCallResult Ok(object? data = null) => new()
        {
            Success = true,
            Data = data
        };

        public static ApiCallResult Error(string error) => new()
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

