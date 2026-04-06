using System.Collections.Generic;
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
    /// <returns>Installed on-disk components; <see cref="AIModelsSetupStatus.RequiredMissing"/> lists tier-required components; optional vision zips are listed separately.</returns>
    Task<AIModelsSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// HTTP Content-Length for each release zip (when the server returns it). Used for transparent size estimates in the UI.
    /// </summary>
    Task<IReadOnlyDictionary<string, long>> GetComponentDownloadSizesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and extracts missing model zips into the app's models directory.
    /// Skips components that are already on disk.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0.0–1.0 and message).</param>
    /// <param name="optionalAdditionalComponents">
    /// Optional component names to include (e.g. "high-image"). Only applied when that component is listed in
    /// <see cref="AIModelsSetupStatus.OptionalImageMissing"/> for the current tier.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with list of newly installed items, or failure with error message.</returns>
    Task<Result<AIModelsSetupResult>> DownloadAndExtractMissingAsync(
        IProgress<AIModelsSetupProgress>? progress,
        IReadOnlySet<string>? optionalAdditionalComponents,
        CancellationToken cancellationToken = default);
}
