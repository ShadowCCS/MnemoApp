using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Resolves import/export adapters and executes content format operations.
/// </summary>
public interface IImportExportCoordinator
{
    IReadOnlyList<ImportExportCapability> GetCapabilities(string? contentType = null);

    Task<Result<ImportExportPreview>> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImportExportResult>> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImportExportResult>> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default);
}
