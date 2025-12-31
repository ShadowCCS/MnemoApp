using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IEmbeddingService
{
    Task<Result<float[]>> GetEmbeddingAsync(string text, CancellationToken ct = default);
}