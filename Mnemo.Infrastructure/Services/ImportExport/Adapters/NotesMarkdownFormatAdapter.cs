using System.Text;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.Infrastructure.Services.ImportExport.Adapters;

public sealed class NotesMarkdownFormatAdapter : IContentFormatAdapter
{
    private readonly INoteService _noteService;

    public NotesMarkdownFormatAdapter(INoteService noteService)
    {
        _noteService = noteService;
    }

    public string ContentType => "notes";

    public string FormatId => "notes.markdown";

    public string DisplayName => "Markdown (.md)";

    public IReadOnlyList<string> Extensions => [".md"];

    public bool SupportsImport => true;

    public bool SupportsExport => true;

    public Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ImportExportPreview
        {
            CanImport = true,
            ContentType = ContentType,
            FormatId = FormatId,
            DiscoveredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["notes"] = 1 }
        });
    }

    public async Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        var markdown = await File.ReadAllTextAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
        var title = Path.GetFileNameWithoutExtension(request.FilePath);
        var note = new Note
        {
            NoteId = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(title) ? "Imported Note" : title,
            Content = markdown,
            Blocks = NoteBlockMarkdownConverter.Deserialize(markdown)
        };

        var save = await _noteService.SaveNoteAsync(note).ConfigureAwait(false);
        return new ImportExportResult
        {
            Success = save.IsSuccess,
            ContentType = ContentType,
            FormatId = FormatId,
            ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["notes"] = save.IsSuccess ? 1 : 0 },
            ErrorMessage = save.IsSuccess ? null : save.ErrorMessage
        };
    }

    public async Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Payload is not Note note)
        {
            return new ImportExportResult
            {
                Success = false,
                ContentType = ContentType,
                FormatId = FormatId,
                ErrorMessage = "Markdown export requires a Note payload."
            };
        }

        var markdown = note.Blocks is { Count: > 0 }
            ? NoteBlockMarkdownConverter.Serialize(note.Blocks)
            : note.Content ?? string.Empty;
        await File.WriteAllTextAsync(request.FilePath, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return new ImportExportResult
        {
            Success = true,
            ContentType = ContentType,
            FormatId = FormatId,
            ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["notes"] = 1 }
        };
    }
}
