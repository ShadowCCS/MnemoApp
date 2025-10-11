using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.Services.FileProcessing;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// API for file processing operations
    /// </summary>
    public class FileApi
    {
        private readonly IFileProcessingService _fileProcessingService;
        private readonly FileProcessorRegistry _registry;

        public FileApi(IFileProcessingService fileProcessingService, FileProcessorRegistry registry)
        {
            _fileProcessingService = fileProcessingService;
            _registry = registry;
        }

        /// <summary>
        /// Process a single file and extract its text content
        /// </summary>
        public async Task<FileProcessingResult> ProcessFile(string filePath, CancellationToken cancellationToken = default)
        {
            return await _fileProcessingService.ProcessFileAsync(filePath, cancellationToken);
        }

        /// <summary>
        /// Process multiple files and combine their content into a formatted string
        /// </summary>
        public async Task<string> ProcessFiles(string[] filePaths, CancellationToken cancellationToken = default)
        {
            return await _fileProcessingService.ProcessFilesAsync(filePaths, cancellationToken);
        }

        /// <summary>
        /// Check if a file type is supported
        /// </summary>
        public bool IsFileSupported(string filePath)
        {
            return _fileProcessingService.IsFileSupported(filePath);
        }

        /// <summary>
        /// Get all supported file extensions
        /// </summary>
        public string[] GetSupportedExtensions()
        {
            return _fileProcessingService.GetSupportedExtensions();
        }

        /// <summary>
        /// Register a custom file processor (for extensions)
        /// </summary>
        public void RegisterProcessor(IFileProcessor processor)
        {
            _registry.RegisterProcessor(processor);
        }

        /// <summary>
        /// Get all registered processor names
        /// </summary>
        public string[] GetRegisteredProcessors()
        {
            return System.Linq.Enumerable.ToArray(_registry.GetRegisteredProcessorNames());
        }
    }
}

