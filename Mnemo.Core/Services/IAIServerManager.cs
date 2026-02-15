using System;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Manages lifecycle of AI model servers (e.g. llama.cpp). Implementations are disposable
/// and should stop all server processes when disposed.
/// </summary>
public interface IAIServerManager : IDisposable
{
    /// <summary>
    /// Ensures the server for the given model is running and ready.
    /// </summary>
    Task EnsureRunningAsync(AIModelManifest manifest, System.Threading.CancellationToken ct);
}
