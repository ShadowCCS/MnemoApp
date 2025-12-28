using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IVectorStore
{
    Task SaveChunksAsync(IEnumerable<KnowledgeChunk> chunks);
    Task<IEnumerable<KnowledgeChunk>> SearchAsync(float[] queryVector, int limit = 5);
    Task DeleteBySourceAsync(string sourceId);
}


