using System;
using System.Threading.Tasks;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Optional interface for extensions that contribute UI elements
    /// </summary>
    public interface IUIContributor
    {
        /// <summary>
        /// Register UI views, sidebar items, etc.
        /// </summary>
        Task RegisterUIAsync(IExtensionContext context);
    }
}

