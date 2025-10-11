using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services.FileProcessing.Processors
{
    /// <summary>
    /// Processor for JSON files - formats them nicely for AI consumption
    /// </summary>
    public class JsonFileProcessor : IFileProcessor
    {
        public string ProcessorName => "JsonFileProcessor";

        public string[] SupportedExtensions => new[] { ".json", ".jsonc" };

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
                var rawContent = await File.ReadAllTextAsync(filePath, cancellationToken);

                // Try to parse and format the JSON for better readability
                string formattedContent;
                try
                {
                    var jsonDocument = JsonDocument.Parse(rawContent);
                    formattedContent = JsonSerializer.Serialize(jsonDocument, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }
                catch
                {
                    // If parsing fails, just use raw content
                    formattedContent = rawContent;
                }

                return FileProcessingResult.CreateSuccess(
                    formattedContent,
                    fileInfo.Name,
                    fileInfo.Extension,
                    fileInfo.Length
                );
            }
            catch (Exception ex)
            {
                return FileProcessingResult.CreateFailure(
                    $"Failed to read JSON file: {ex.Message}",
                    Path.GetFileName(filePath)
                );
            }
        }
    }
}

