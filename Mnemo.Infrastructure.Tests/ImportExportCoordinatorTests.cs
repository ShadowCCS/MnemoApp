using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.ImportExport;

namespace Mnemo.Infrastructure.Tests;

public sealed class ImportExportCoordinatorTests
{
    [Fact]
    public async Task ImportAsync_ResolvesByFormatAndContentType()
    {
        var notesAdapter = new StubAdapter("notes", "notes.markdown", [".md"]);
        var flashcardsAdapter = new StubAdapter("flashcards", "flashcards.csv", [".csv"]);
        var coordinator = new ImportExportCoordinator([notesAdapter, flashcardsAdapter]);

        var result = await coordinator.ImportAsync(new ImportExportRequest
        {
            ContentType = "notes",
            FormatId = "notes.markdown",
            FilePath = "x.md"
        }).ConfigureAwait(false);

        Assert.True(result.IsSuccess);
        Assert.Equal("notes.markdown", result.Value?.FormatId);
    }

    [Fact]
    public async Task ImportAsync_ResolvesByExtensionWhenFormatMissing()
    {
        var notesAdapter = new StubAdapter("notes", "notes.markdown", [".md"]);
        var coordinator = new ImportExportCoordinator([notesAdapter]);

        var result = await coordinator.ImportAsync(new ImportExportRequest
        {
            ContentType = "notes",
            FilePath = "x.md"
        }).ConfigureAwait(false);

        Assert.True(result.IsSuccess);
        Assert.Equal("notes.markdown", result.Value?.FormatId);
    }

    private sealed class StubAdapter : IContentFormatAdapter
    {
        public StubAdapter(string contentType, string formatId, IReadOnlyList<string> extensions)
        {
            ContentType = contentType;
            FormatId = formatId;
            Extensions = extensions;
        }

        public string ContentType { get; }
        public string FormatId { get; }
        public string DisplayName => FormatId;
        public IReadOnlyList<string> Extensions { get; }
        public bool SupportsImport => true;
        public bool SupportsExport => true;

        public Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportExportPreview { CanImport = true, ContentType = ContentType, FormatId = FormatId });

        public Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportExportResult { Success = true, ContentType = ContentType, FormatId = FormatId });

        public Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportExportResult { Success = true, ContentType = ContentType, FormatId = FormatId });
    }
}
