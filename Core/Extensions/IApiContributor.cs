using System;
using System.Threading.Tasks;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Optional interface for extensions that expose custom API endpoints
    /// </summary>
    public interface IApiContributor
    {
        /// <summary>
        /// Register custom API endpoints
        /// </summary>
        Task RegisterApiAsync(IExtensionContext context);
        
        /// <summary>
        /// Handle API calls to this extension
        /// </summary>
        Task<object?> HandleApiCallAsync(string endpoint, object? parameters);
    }
}

