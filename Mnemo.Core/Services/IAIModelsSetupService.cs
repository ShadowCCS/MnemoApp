using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Service for downloading and extracting AI model zips from a release (e.g. GitHub).
/// </summary>
public interface IAIModelsSetupService
{
    /// <summary>
    /// Returns which setup components are already installed and which are missing.
    /// Use this to show "finished" when all are installed, or to download only missing components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status with Installed and Missing lists (e.g. "bge-small", "server", "router", "fast").</returns>
    Task<AIModelsSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and extracts only the missing model zips into the app's models directory.
    /// Components that are already installed are skipped.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0.0–1.0 and message).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with list of newly installed items, or failure with error message.</returns>
    Task<Result<AIModelsSetupResult>> DownloadAndExtractMissingAsync(
        IProgress<AIModelsSetupProgress>? progress,
        CancellationToken cancellationToken = default);
}
