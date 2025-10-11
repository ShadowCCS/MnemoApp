using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services.FileProcessing
{
    /// <summary>
    /// Result of file processing operation
    /// </summary>
    public record FileProcessingResult
    {
        public bool Success { get; init; }
        public string? Content { get; init; }
        public string? ErrorMessage { get; init; }
        public string? FileName { get; init; }
        public string? FileExtension { get; init; }
        public long? FileSize { get; init; }

        public static FileProcessingResult CreateSuccess(string content, string fileName, string fileExtension, long fileSize)
        {
            return new FileProcessingResult
            {
                Success = true,
                Content = content,
                FileName = fileName,
                FileExtension = fileExtension,
                FileSize = fileSize
            };
        }

        public static FileProcessingResult CreateFailure(string errorMessage, string fileName)
        {
            return new FileProcessingResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                FileName = fileName
            };
        }
    }

    /// <summary>
    /// Interface for file processors that extract text content from files
    /// </summary>
    public interface IFileProcessor
    {
        /// <summary>
        /// Name of the processor (e.g., "TextFileProcessor")
        /// </summary>
        string ProcessorName { get; }

        /// <summary>
        /// File extensions this processor can handle (e.g., [".txt", ".text"])
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// Check if this processor can handle a file with the given extension
        /// </summary>
        bool CanHandle(string fileExtension);

        /// <summary>
        /// Process a file and extract its text content
        /// </summary>
        Task<FileProcessingResult> ProcessAsync(string filePath, CancellationToken cancellationToken = default);
    }
}

