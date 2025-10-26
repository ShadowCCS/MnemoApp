using System;
using System.Threading.Tasks;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Base interface for all MnemoApp extensions
    /// </summary>
    public interface IMnemoExtension
    {
        /// <summary>
        /// Called when the extension is first loaded
        /// </summary>
        Task OnLoadAsync(IExtensionContext context);
        
        /// <summary>
        /// Called when the extension is unloaded
        /// </summary>
        Task OnUnloadAsync();
        
        /// <summary>
        /// Called when the extension is enabled
        /// </summary>
        Task OnEnableAsync();
        
        /// <summary>
        /// Called when the extension is disabled
        /// </summary>
        Task OnDisableAsync();
        
        /// <summary>
        /// Called periodically to allow extension to perform updates (optional)
        /// </summary>
        Task OnUpdateAsync() => Task.CompletedTask;
    }
}

