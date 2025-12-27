using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Defines a service for processing files.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Gets a collection of file extensions supported by the service.
    /// </summary>
    /// <returns>A collection of file extensions (e.g., ".txt", ".pdf").</returns>
    IEnumerable<string> GetSupportedExtensions();

    /// <summary>
    /// Processes a file and extracts its content.
    /// </summary>
    /// <param name="filePath">The path to the file to process.</param>
    /// <returns>A result containing success status, content, and any error message.</returns>
    Task<FileProcessResult> ProcessFileAsync(string filePath);
}

public record FileProcessResult(bool Success, string? Content, string? ErrorMessage);

