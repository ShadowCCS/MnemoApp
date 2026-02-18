using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IKnowledgeService
{
    Task<Result> IngestDocumentAsync(string path, string? scopeId = null, CancellationToken ct = default);
    Task<Result<IEnumerable<KnowledgeChunk>>> SearchAsync(string query, int limit = 5, string? scopeId = null, CancellationToken ct = default);
    Task<Result> RemoveSourceAsync(string sourceId, CancellationToken ct = default);
    Task<Result> RemoveScopeAsync(string scopeId, CancellationToken ct = default);
}

