using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IEmbeddingService
{
    Task<Result<float[]>> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>Embeds multiple texts in one or few ONNX runs (batched). Order matches <paramref name="texts"/>.</summary>
    Task<Result<IReadOnlyList<float[]>>> GetEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}