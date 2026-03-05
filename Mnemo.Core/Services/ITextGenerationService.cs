using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ITextGenerationService : IDisposable
{
    /// <param name="responseJsonSchema">Optional. When set, request is sent with response_format so the server (e.g. Llama) forces JSON output matching this schema; same mechanism as router.</param>
    Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct, object? responseJsonSchema = null);
    /// <param name="imageBase64Contents">Optional. For vision models: list of image bytes as base64 strings (without data URL prefix).</param>
    IAsyncEnumerable<string> GenerateStreamingAsync(AIModelManifest manifest, string prompt, CancellationToken ct, IReadOnlyList<string>? imageBase64Contents = null);
    void UnloadModel(string modelId);
}


