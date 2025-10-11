using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace MnemoApp.Core.Services.FileProcessing.Processors
{
    /// <summary>
    /// Processor for PDF files using PdfPig
    /// </summary>
    public class PdfFileProcessor : IFileProcessor
    {
        public string ProcessorName => "PdfFileProcessor";

        public string[] SupportedExtensions => new[] { ".pdf" };

        public bool CanHandle(string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
                return false;

            var ext = fileExtension.ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        public Task<FileProcessingResult> ProcessAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                    return Task.FromResult(FileProcessingResult.CreateFailure("File not found", Path.GetFileName(filePath)));

                var fileInfo = new FileInfo(filePath);
                var content = new StringBuilder();

                using (var document = PdfDocument.Open(filePath))
                {
                    foreach (var page in document.GetPages())
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var pageText = page.Text;
                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            content.AppendLine($"--- Page {page.Number} ---");
                            content.AppendLine(pageText);
                            content.AppendLine();
                        }
                    }
                }

                var extractedText = content.ToString().Trim();
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return Task.FromResult(FileProcessingResult.CreateFailure(
                        "No text content could be extracted from PDF",
                        fileInfo.Name
                    ));
                }

                return Task.FromResult(FileProcessingResult.CreateSuccess(
                    extractedText,
                    fileInfo.Name,
                    fileInfo.Extension,
                    fileInfo.Length
                ));
            }
            catch (Exception ex)
            {
                return Task.FromResult(FileProcessingResult.CreateFailure(
                    $"Failed to read PDF file: {ex.Message}",
                    Path.GetFileName(filePath)
                ));
            }
        }
    }
}

