using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Provides import/export support for one content type and external format.
/// </summary>
public interface IContentFormatAdapter
{
    string ContentType { get; }

    string FormatId { get; }

    string DisplayName { get; }

    IReadOnlyList<string> Extensions { get; }

    bool SupportsImport { get; }

    bool SupportsExport { get; }

    Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);

    Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);

    Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);
}
