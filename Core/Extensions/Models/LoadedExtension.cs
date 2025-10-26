using MnemoApp.Core.Extensions;

namespace MnemoApp.Core.Extensions.Models
{
    /// <summary>
    /// Represents a loaded extension with its runtime context
    /// </summary>
    public class LoadedExtension
    {
        /// <summary>
        /// Extension metadata
        /// </summary>
        public required ExtensionMetadata Metadata { get; init; }
        
        /// <summary>
        /// Extension instance
        /// </summary>
        public required IMnemoExtension Instance { get; init; }
        
        /// <summary>
        /// Extension context
        /// </summary>
        public required IExtensionContext Context { get; init; }
    }
}

