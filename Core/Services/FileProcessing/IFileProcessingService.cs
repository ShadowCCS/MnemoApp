using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services.FileProcessing
{
    /// <summary>
    /// Service for processing uploaded files and extracting their text content
    /// </summary>
    public interface IFileProcessingService
    {
        /// <summary>
        /// Process a single file and extract its content
        /// </summary>
        Task<FileProcessingResult> ProcessFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Process multiple files and combine their content
        /// </summary>
        Task<string> ProcessFilesAsync(string[] filePaths, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a file type is supported
        /// </summary>
        bool IsFileSupported(string filePath);

        /// <summary>
        /// Get all supported file extensions
        /// </summary>
        string[] GetSupportedExtensions();
    }
}

