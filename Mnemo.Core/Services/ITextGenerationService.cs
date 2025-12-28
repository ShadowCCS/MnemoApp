using System;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ITextGenerationService : IDisposable
{
    Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct);
    IAsyncEnumerable<string> GenerateStreamingAsync(AIModelManifest manifest, string prompt, CancellationToken ct);
    void UnloadModel(string modelId);
}


