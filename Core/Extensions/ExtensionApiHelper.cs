using System;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Extensions.Services;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Helper for extensions to register API endpoints
    /// </summary>
    public class ExtensionApiHelper
    {
        private readonly ExtensionApiRouter _router;
        private readonly string _extensionName;

        internal ExtensionApiHelper(ExtensionApiRouter router, string extensionName)
        {
            _router = router;
            _extensionName = extensionName;
        }

        /// <summary>
        /// Register an API endpoint
        /// </summary>
        /// <param name="endpoint">Endpoint name (e.g., "getData", "processItem")</param>
        /// <param name="requiredPermissions">Permissions required to call this endpoint</param>
        public void RegisterEndpoint(string endpoint, ExtensionPermission requiredPermissions = ExtensionPermission.ApiRegistration)
        {
            _router.RegisterEndpoint(_extensionName, endpoint, requiredPermissions);
        }

        /// <summary>
        /// Check if an endpoint is registered
        /// </summary>
        public bool IsEndpointRegistered(string endpoint)
        {
            return _router.IsEndpointRegistered(_extensionName, endpoint);
        }
    }
}

