namespace MnemoApp.Core.Extensions.Models
{
    /// <summary>
    /// How an extension is loaded
    /// </summary>
    public enum ExtensionLoadMode
    {
        /// <summary>
        /// Extension is loaded from a compiled DLL
        /// </summary>
        CompiledAssembly,
        
        /// <summary>
        /// Extension is loaded from source code (development mode)
        /// </summary>
        SourceBased
    }
}

