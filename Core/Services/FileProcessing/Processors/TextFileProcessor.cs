using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services.FileProcessing.Processors
{
    /// <summary>
    /// Processor for plain text files
    /// </summary>
    public class TextFileProcessor : IFileProcessor
    {
        public string ProcessorName => "TextFileProcessor";

        public string[] SupportedExtensions => new[] { ".txt", ".text", ".log", ".md", ".markdown" };

        public bool CanHandle(string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
                return false;

            var ext = fileExtension.ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        public async Task<FileProcessingResult> ProcessAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                    return FileProcessingResult.CreateFailure("File not found", Path.GetFileName(filePath));

                var fileInfo = new FileInfo(filePath);
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);

                return FileProcessingResult.CreateSuccess(
                    content,
                    fileInfo.Name,
                    fileInfo.Extension,
                    fileInfo.Length
                );
            }
            catch (Exception ex)
            {
                return FileProcessingResult.CreateFailure(
                    $"Failed to read text file: {ex.Message}",
                    Path.GetFileName(filePath)
                );
            }
        }
    }
}

