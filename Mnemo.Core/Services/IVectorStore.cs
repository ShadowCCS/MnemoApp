using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IVectorStore
{
    Task SaveChunksAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default);
    Task<IEnumerable<KnowledgeChunk>> SearchAsync(float[] queryVector, int limit = 5, CancellationToken ct = default);
    Task DeleteBySourceAsync(string sourceId, CancellationToken ct = default);
}

