using System;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IResourceGovernor : IDisposable
{
    event Action<string>? ModelShouldUnload;
    Task<bool> AcquireModelAsync(AIModelManifest manifest, CancellationToken ct);
    void ReleaseModel(AIModelManifest manifest);
}


