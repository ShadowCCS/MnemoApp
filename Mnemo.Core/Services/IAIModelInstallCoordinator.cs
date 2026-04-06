using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Runs AI model setup downloads independently of UI lifetime so installs continue after closing overlays.
/// </summary>
public interface IAIModelInstallCoordinator
{
    bool IsRunning { get; }

    /// <summary>Fired on a background thread when download/extract progress updates.</summary>
    event Action<AIModelsSetupProgress>? ProgressChanged;

    /// <summary>Fired once when the current download attempt finishes (success or failure).</summary>
    event Action<Result<AIModelsSetupResult>>? Completed;

    /// <summary>
    /// Starts a download if none is active; otherwise returns the in-flight task so callers can await the same work.
    /// </summary>
    Task<Result<AIModelsSetupResult>> StartDownloadAsync(
        IReadOnlySet<string>? optionalAdditionalComponents,
        CancellationToken cancellationToken = default);
}
