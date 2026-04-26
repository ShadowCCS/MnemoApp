using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.ImportExport.Adapters;

/// <summary>
/// Reserved adapter contract for future Anki (.apkg) support.
/// </summary>
public sealed class FlashcardsAnkiFormatAdapter : IContentFormatAdapter
{
    public string ContentType => "flashcards";
    public string FormatId => "flashcards.anki";
    public string DisplayName => "Anki Package (.apkg)";
    public IReadOnlyList<string> Extensions => [".apkg"];
    public bool SupportsImport => false;
    public bool SupportsExport => false;

    public Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ImportExportPreview
        {
            CanImport = false,
            ContentType = ContentType,
            FormatId = FormatId,
            Warnings = { "Anki import is not implemented yet." }
        });
    }

    public Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ImportExportResult
        {
            Success = false,
            ContentType = ContentType,
            FormatId = FormatId,
            ErrorMessage = "Anki import is not implemented yet."
        });
    }

    public Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ImportExportResult
        {
            Success = false,
            ContentType = ContentType,
            FormatId = FormatId,
            ErrorMessage = "Anki export is not implemented yet."
        });
    }
}
