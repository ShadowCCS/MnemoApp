using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface INotePdfExportService
{
    Task<byte[]> GeneratePdfAsync(Note note, NotePdfExportOptions options, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<byte[]>> GeneratePreviewPngPagesAsync(Note note, NotePdfExportOptions options, CancellationToken cancellationToken = default);
}
