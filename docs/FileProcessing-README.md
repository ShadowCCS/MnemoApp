# File Processing Service

## Overview

The File Processing Service provides a modular, extensible system for processing uploaded files and extracting their text content for AI consumption. It follows the registry pattern similar to the AI driver system.

## Features

- **Modular Architecture**: Easily add support for new file types
- **Built-in Processors**: TXT, JSON, PDF, DOCX support out of the box
- **Extension Support**: Extensions can register custom processors via MnemoAPI
- **No Task System**: Fast, synchronous-style processing without UI toasts/overlays
- **Simple Output Format**: Clean string output with file boundaries

## Built-in File Types

- **Text Files**: `.txt`, `.text`, `.log`, `.md`, `.markdown`
- **JSON Files**: `.json`, `.jsonc`
- **PDF Files**: `.pdf` (via PdfPig)
- **DOCX Files**: `.docx` (via DocumentFormat.OpenXml)

## Usage

### Via MnemoAPI (Recommended)

```csharp
// Process a single file
var result = await mnemoAPI.files.ProcessFile(filePath);
if (result.Success)
{
    Console.WriteLine(result.Content);
}
else
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}

// Process multiple files
var content = await mnemoAPI.files.ProcessFiles(new[] { file1, file2, file3 });
// Returns formatted string with all file contents

// Check if file type is supported
bool isSupported = mnemoAPI.files.IsFileSupported("document.pdf");

// Get all supported extensions
string[] extensions = mnemoAPI.files.GetSupportedExtensions();
```

### Direct Service Usage

```csharp
// Via dependency injection
public MyClass(IFileProcessingService fileService)
{
    _fileService = fileService;
}

// Process files
var result = await _fileService.ProcessFileAsync(filePath);
```

## Creating Custom Processors

Extensions can register custom file processors to support additional file types:

### 1. Implement IFileProcessor

```csharp
public class CustomProcessor : IFileProcessor
{
    public string ProcessorName => "CustomProcessor";
    
    public string[] SupportedExtensions => new[] { ".custom", ".ext" };
    
    public bool CanHandle(string fileExtension)
    {
        return fileExtension.ToLowerInvariant() == ".custom" ||
               fileExtension.ToLowerInvariant() == ".ext";
    }
    
    public async Task<FileProcessingResult> ProcessAsync(
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Your extraction logic here
            var content = await ExtractContentFromCustomFile(filePath);
            
            return FileProcessingResult.CreateSuccess(
                content,
                Path.GetFileName(filePath),
                Path.GetExtension(filePath),
                new FileInfo(filePath).Length
            );
        }
        catch (Exception ex)
        {
            return FileProcessingResult.CreateFailure(
                ex.Message,
                Path.GetFileName(filePath)
            );
        }
    }
}
```

### 2. Register via MnemoAPI

```csharp
// In your extension initialization
mnemoAPI.files.RegisterProcessor(new CustomProcessor());
```

## Output Format

When processing multiple files, the service returns a formatted string:

```
=== File: document.pdf ===
[extracted content from PDF]

=== File: notes.txt ===
[extracted content from TXT]

=== File: data.json ===
[formatted JSON content]
```

Failed files include error information:

```
=== File: corrupted.pdf (Error) ===
Error: Failed to read PDF file: The file is corrupted
```

## InputBuilder Integration

The InputBuilder component automatically uses this service when files are uploaded:

1. User clicks the file upload area
2. File picker opens with supported extensions
3. Selected files are processed immediately
4. Processed content is stored in `FileItemViewModel`
5. When Generate is clicked, all file contents are included in the output

## Architecture

```
FileProcessingService
├── FileProcessorRegistry
│   ├── TextFileProcessor
│   ├── JsonFileProcessor
│   ├── PdfFileProcessor
│   ├── DocxFileProcessor
│   └── [Custom Processors]
└── FileApi (MnemoAPI)
```

## Error Handling

All processors return `FileProcessingResult` with:
- `Success`: Boolean indicating if processing succeeded
- `Content`: Extracted text content (if successful)
- `ErrorMessage`: Error description (if failed)
- `FileName`, `FileExtension`, `FileSize`: Metadata

The service gracefully handles errors and returns structured error information rather than throwing exceptions.

## Performance

- Processing is synchronous (no task scheduler overhead)
- No UI toasts or overlays are triggered
- Fast for typical file sizes (< 1MB)
- Consider implementing chunking for very large files in the future

## Future Enhancements

Potential improvements that could be added:

1. **Chunking**: Split large files into manageable chunks
2. **Caching**: Cache processed content temporarily
3. **Progress Reporting**: For large files, report extraction progress
4. **Additional Formats**: Excel, PowerPoint, HTML, etc.
5. **OCR Support**: Extract text from images in PDFs
6. **Metadata Extraction**: Extract author, creation date, etc.

