using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MnemoApp.Core.Services.FileProcessing.Processors
{
    /// <summary>
    /// Processor for DOCX files using DocumentFormat.OpenXml
    /// </summary>
    public class DocxFileProcessor : IFileProcessor
    {
        public string ProcessorName => "DocxFileProcessor";

        public string[] SupportedExtensions => new[] { ".docx" };

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

                using (var document = WordprocessingDocument.Open(filePath, false))
                {
                    if (document.MainDocumentPart?.Document?.Body == null)
                    {
                        return Task.FromResult(FileProcessingResult.CreateFailure(
                            "Invalid DOCX file structure",
                            fileInfo.Name
                        ));
                    }

                    var body = document.MainDocumentPart.Document.Body;
                    
                    foreach (var element in body.Elements())
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        if (element is Paragraph paragraph)
                        {
                            var paragraphText = paragraph.InnerText;
                            if (!string.IsNullOrWhiteSpace(paragraphText))
                            {
                                content.AppendLine(paragraphText);
                            }
                        }
                        else if (element is Table table)
                        {
                            content.AppendLine("\n[Table]");
                            foreach (var row in table.Elements<TableRow>())
                            {
                                var rowText = string.Join(" | ", row.Elements<TableCell>()
                                    .Select(cell => cell.InnerText.Trim()));
                                if (!string.IsNullOrWhiteSpace(rowText))
                                {
                                    content.AppendLine(rowText);
                                }
                            }
                            content.AppendLine();
                        }
                    }
                }

                var extractedText = content.ToString().Trim();
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return Task.FromResult(FileProcessingResult.CreateFailure(
                        "No text content could be extracted from DOCX",
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
                    $"Failed to read DOCX file: {ex.Message}",
                    Path.GetFileName(filePath)
                ));
            }
        }
    }
}

