using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.Services.FileProcessing.Processors;

namespace MnemoApp.Core.Services.FileProcessing
{
    /// <summary>
    /// Service for processing uploaded files and extracting their text content
    /// </summary>
    public class FileProcessingService : IFileProcessingService
    {
        private readonly FileProcessorRegistry _registry;

        public FileProcessingService(FileProcessorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            
            // Register built-in processors
            RegisterBuiltInProcessors();
        }

        private void RegisterBuiltInProcessors()
        {
            _registry.RegisterProcessor(new TextFileProcessor());
            _registry.RegisterProcessor(new JsonFileProcessor());
            _registry.RegisterProcessor(new PdfFileProcessor());
            _registry.RegisterProcessor(new DocxFileProcessor());
        }

        public async Task<FileProcessingResult> ProcessFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return FileProcessingResult.CreateFailure("File path is empty", "");
            }

            if (!File.Exists(filePath))
            {
                return FileProcessingResult.CreateFailure("File not found", Path.GetFileName(filePath));
            }

            var processor = _registry.GetProcessorForFile(filePath);
            if (processor == null)
            {
                var extension = Path.GetExtension(filePath);
                return FileProcessingResult.CreateFailure(
                    $"No processor found for file type '{extension}'",
                    Path.GetFileName(filePath)
                );
            }

            try
            {
                return await processor.ProcessAsync(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                return FileProcessingResult.CreateFailure(
                    $"Processing failed: {ex.Message}",
                    Path.GetFileName(filePath)
                );
            }
        }

        public async Task<string> ProcessFilesAsync(string[] filePaths, CancellationToken cancellationToken = default)
        {
            if (filePaths == null || filePaths.Length == 0)
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            var processedCount = 0;

            foreach (var filePath in filePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fileResult = await ProcessFileAsync(filePath, cancellationToken);
                
                if (fileResult.Success && !string.IsNullOrWhiteSpace(fileResult.Content))
                {
                    if (processedCount > 0)
                    {
                        result.AppendLine();
                    }

                    result.AppendLine($"=== File: {fileResult.FileName} ===");
                    result.AppendLine(fileResult.Content);
                    processedCount++;
                }
                else
                {
                    // Include error information
                    if (processedCount > 0)
                    {
                        result.AppendLine();
                    }
                    result.AppendLine($"=== File: {fileResult.FileName} (Error) ===");
                    result.AppendLine($"Error: {fileResult.ErrorMessage}");
                    processedCount++;
                }
            }

            return result.ToString();
        }

        public bool IsFileSupported(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return _registry.IsExtensionSupported(extension);
        }

        public string[] GetSupportedExtensions()
        {
            return _registry.GetSupportedExtensions().ToArray();
        }
    }
}

