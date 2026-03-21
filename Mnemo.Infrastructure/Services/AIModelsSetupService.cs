using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;

namespace Mnemo.Infrastructure.Services;

public class AIModelsSetupService : IAIModelsSetupService
{
    private const string ReleaseBaseUrl = "https://github.com/ShadowCCS/MnemoApp/releases/download/Models/";
    private const int DownloadRetryCount = 2;
    /// <summary>Max concurrent HTTP downloads (batch) to avoid saturating the link or the host.</summary>
    private const int MaxConcurrentDownloads = 6;
    /// <summary>Share of overall progress [0,1] used for the download phase; the rest is extraction + registry.</summary>
    private const double DownloadPhaseProgressWeight = 0.65;

    /// <summary>
    /// Release zips: <c>manager.zip</c>, <c>low.zip</c>, <c>middle.zip</c>, <c>high.zip</c>, <c>high-image.zip</c>.
    /// <c>high</c> / <c>high-image</c> both extract into <c>text/high</c> (GGUF vs mmproj for vision); see <see cref="ModelRegistry"/>.
    /// Text bundles per hardware tier: Low → manager + low; Mid → + middle; High → + high + high-image (not middle).
    /// </summary>
    private static readonly (string Name, string FileName, string RelativePath)[] Entries =
    {
        ("bge-small", "bge-small.zip", Path.Combine("embedding", "bge-small")),
        ("server", "server.zip", "llamaServer"),
        ("manager", "manager.zip", Path.Combine("text", "manager")),
        ("low", "low.zip", Path.Combine("text", "low")),
        ("mid", "middle.zip", Path.Combine("text", "mid")),
        ("high", "high.zip", Path.Combine("text", "high")),
        ("high-image", "high-image.zip", Path.Combine("text", "high")),
        ("stt", "stt.zip", Path.Combine("audio", "STT")),
    };

    private readonly string _modelsPath;
    private readonly IAIModelRegistry _modelRegistry;
    private readonly ISettingsService _settingsService;
    private readonly HardwareDetector _hardwareDetector;
    private readonly IHardwareTierEvaluator _hardwareTierEvaluator;
    private readonly HttpClient _httpClient;

    public AIModelsSetupService(
        IAIModelRegistry modelRegistry,
        ISettingsService settingsService,
        HardwareDetector hardwareDetector,
        IHardwareTierEvaluator hardwareTierEvaluator)
    {
        _modelRegistry = modelRegistry;
        _settingsService = settingsService;
        _hardwareDetector = hardwareDetector;
        _hardwareTierEvaluator = hardwareTierEvaluator;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _modelsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mnemo",
            "models");
    }

    public Task<AIModelsSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        _hardwareDetector.Refresh();
        var tier = _hardwareTierEvaluator.EvaluateTier(_hardwareDetector.Detect());

        var installed = new List<string>();
        var missing = new List<string>();

        foreach (var (name, _, relativePath) in Entries)
        {
            var targetDir = Path.Combine(_modelsPath, relativePath);
            if (IsComponentInstalled(name, targetDir))
                installed.Add(name);
            else if (IsRequiredForTier(name, tier))
                missing.Add(name);
        }

        var status = new AIModelsSetupStatus { Installed = installed, Missing = missing };
        return Task.FromResult(status);
    }

    /// <summary>
    /// Embedding, server, manager, STT, and <c>low</c> chat are always required.
    /// <c>middle.zip</c> only for <see cref="HardwarePerformanceTier.Mid"/>.
    /// <c>high.zip</c> / <c>high-image.zip</c> only for <see cref="HardwarePerformanceTier.High"/>.
    /// </summary>
    private static bool IsRequiredForTier(string name, HardwarePerformanceTier tier)
    {
        return name switch
        {
            "mid" => tier == HardwarePerformanceTier.Mid,
            "high" or "high-image" => tier == HardwarePerformanceTier.High,
            _ => true
        };
    }

    /// <summary>
    /// Server is installed if llama-server.exe exists; STT if ggml-tiny.bin exists;
    /// other components if the directory exists and has files.
    /// </summary>
    private static bool IsComponentInstalled(string name, string targetDir)
    {
        if (!Directory.Exists(targetDir))
            return false;

        if (string.Equals(name, "server", StringComparison.OrdinalIgnoreCase))
        {
            var exePath = Path.Combine(targetDir, "llama-server.exe");
            return File.Exists(exePath);
        }

        if (string.Equals(name, "stt", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = Path.Combine(targetDir, "ggml-tiny.bin");
            return File.Exists(modelPath);
        }

        if (string.Equals(name, "high", StringComparison.OrdinalIgnoreCase))
            return Directory.Exists(targetDir) &&
                   Directory.EnumerateFiles(targetDir, "*.gguf", SearchOption.AllDirectories).Any();

        if (string.Equals(name, "high-image", StringComparison.OrdinalIgnoreCase))
            return Directory.Exists(targetDir) &&
                   Directory.EnumerateFiles(targetDir, "*.mmproj", SearchOption.AllDirectories).Any();

        return Directory.EnumerateFileSystemEntries(targetDir, "*", SearchOption.AllDirectories).Any();
    }

    public async Task<Result<AIModelsSetupResult>> DownloadAndExtractMissingAsync(
        IProgress<AIModelsSetupProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var status = await GetSetupStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.AllInstalled)
        {
            progress?.Report(new AIModelsSetupProgress { Progress = 1.0, Message = "All models already installed." });
            return Result<AIModelsSetupResult>.Success(new AIModelsSetupResult { Installed = Array.Empty<string>() });
        }

        var toInstall = Entries.Where(e => status.Missing.Contains(e.Name)).ToArray();
        var installed = new List<string>();
        var n = toInstall.Length;

        progress?.Report(new AIModelsSetupProgress
        {
            Progress = 0,
            Message = $"Downloading {n} file(s)..."
        });

        var read = new long[n];
        var totalExpected = new long[n];
        var downloadDone = new bool[n];
        var progressLock = new object();

        void ReportDownloadProgress()
        {
            double sum = 0;
            lock (progressLock)
            {
                for (var i = 0; i < n; i++)
                {
                    if (downloadDone[i])
                        sum += 1;
                    else if (totalExpected[i] > 0)
                        sum += (double)read[i] / totalExpected[i];
                }
            }

            var frac = n > 0 ? sum / n : 1;
            progress?.Report(new AIModelsSetupProgress
            {
                Progress = DownloadPhaseProgressWeight * frac,
                Message = null
            });
        }

        using var downloadLimiter = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
        var downloadTasks = new Task<Result<string>>[n];

        async Task<Result<string>> DownloadSlotAsync(int slotIndex, string downloadUrl)
        {
            await downloadLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await DownloadWithRetryAsync(
                    downloadUrl,
                    slotIndex,
                    (idx, byteRead, total) =>
                    {
                        lock (progressLock)
                        {
                            read[idx] = byteRead;
                            if (total > 0)
                                totalExpected[idx] = total;
                        }

                        ReportDownloadProgress();
                    },
                    () =>
                    {
                        lock (progressLock)
                        {
                            downloadDone[slotIndex] = true;
                            if (totalExpected[slotIndex] > 0)
                                read[slotIndex] = totalExpected[slotIndex];
                        }

                        ReportDownloadProgress();
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                downloadLimiter.Release();
            }
        }

        for (var i = 0; i < n; i++)
            downloadTasks[i] = DownloadSlotAsync(i, ReleaseBaseUrl + toInstall[i].FileName);

        Result<string>[] downloadResults;
        try
        {
            downloadResults = await Task.WhenAll(downloadTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        foreach (var r in downloadResults)
        {
            if (!r.IsSuccess)
                return Result<AIModelsSetupResult>.Failure(r.ErrorMessage!, r.Exception);
        }

        var extractBase = DownloadPhaseProgressWeight;
        var extractSpan = 1.0 - extractBase - 0.02;
        for (var i = 0; i < n; i++)
        {
            var (name, fileName, relativePath) = toInstall[i];
            var tempPath = downloadResults[i].Value!;
            var targetDir = Path.Combine(_modelsPath, relativePath);

            progress?.Report(new AIModelsSetupProgress
            {
                Progress = extractBase + extractSpan * (i / (double)n),
                Message = $"Extracting {fileName}..."
            });

            try
            {
                ExtractZipToDirectory(tempPath, targetDir);
            }
            catch (Exception ex)
            {
                return Result<AIModelsSetupResult>.Failure($"Failed to extract {fileName}: {ex.Message}", ex);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }

            installed.Add(name);

            progress?.Report(new AIModelsSetupProgress
            {
                Progress = extractBase + extractSpan * ((i + 1) / (double)n),
                Message = null
            });
        }

        progress?.Report(new AIModelsSetupProgress { Progress = 0.98, Message = "Refreshing model registry..." });

        await _modelRegistry.RefreshAsync().ConfigureAwait(false);

        var serverPath = await _settingsService.GetAsync<string>("AI.LlamaCpp.ServerPath").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            var defaultPath = Path.Combine(_modelsPath, "llamaServer", "llama-server.exe");
            await _settingsService.SetAsync("AI.LlamaCpp.ServerPath", defaultPath).ConfigureAwait(false);
        }

        progress?.Report(new AIModelsSetupProgress { Progress = 1.0, Message = null });
        return Result<AIModelsSetupResult>.Success(new AIModelsSetupResult { Installed = installed });
    }

    private async Task<Result<string>> DownloadWithRetryAsync(
        string url,
        int slotIndex,
        Action<int, long, long>? onChunkProgress,
        Action? onDownloadComplete,
        CancellationToken cancellationToken)
    {
        Exception? lastEx = null;
        for (var attempt = 0; attempt <= DownloadRetryCount; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0L;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var tempPath = Path.Combine(Path.GetTempPath(), "mnemo-setup-" + Guid.NewGuid().ToString("N") + ".zip");
                await using (var fileStream = File.Create(tempPath))
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int count;
                    while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        read += count;
                        onChunkProgress?.Invoke(slotIndex, read, total);
                    }
                }

                onDownloadComplete?.Invoke();
                return Result<string>.Success(tempPath);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt < DownloadRetryCount)
                    await Task.Delay(500 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        return Result<string>.Failure($"Download failed: {lastEx?.Message ?? "Unknown error"}", lastEx);
    }

    /// <summary>
    /// Extracts the zip so that manifest.json (or llama-server.exe) ends up directly in targetDir.
    /// If the zip has a single top-level folder, its contents are moved into targetDir.
    /// </summary>
    private static void ExtractZipToDirectory(string zipPath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var tempDir = Path.Combine(Path.GetTempPath(), "mnemo-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            var topLevel = Directory.GetFileSystemEntries(tempDir);
            var dirs = topLevel.Where(f => Directory.Exists(f)).ToList();
            var files = topLevel.Where(f => File.Exists(f)).ToList();
            if (dirs.Count == 1 && files.Count == 0)
            {
                var sourceDir = dirs[0];
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceDir, file);
                    var dest = Path.Combine(targetDir, relative);
                    var destDir = Path.GetDirectoryName(dest)!;
                    Directory.CreateDirectory(destDir);
                    File.Copy(file, dest, true);
                }
            }
            else
            {
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(tempDir, file);
                    var dest = Path.Combine(targetDir, relative);
                    var destDir = Path.GetDirectoryName(dest)!;
                    Directory.CreateDirectory(destDir);
                    File.Copy(file, dest, true);
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }
}
