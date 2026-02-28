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

namespace Mnemo.Infrastructure.Services;

public class AIModelsSetupService : IAIModelsSetupService
{
    private const string ReleaseBaseUrl = "https://github.com/ShadowCCS/MnemoApp/releases/download/Models/";
    private const int DownloadRetryCount = 2;

    private static readonly (string Name, string FileName, string RelativePath)[] Entries =
    {
        ("bge-small", "bge-small.zip", Path.Combine("embedding", "bge-small")),
        ("server", "server.zip", "llamaServer"),
        ("router", "router.zip", Path.Combine("text", "router")),
        ("fast", "fast.zip", Path.Combine("text", "fast")),
    };

    private readonly string _modelsPath;
    private readonly IAIModelRegistry _modelRegistry;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public AIModelsSetupService(IAIModelRegistry modelRegistry, ISettingsService settingsService)
    {
        _modelRegistry = modelRegistry;
        _settingsService = settingsService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _modelsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mnemo",
            "models");
    }

    public Task<AIModelsSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        var installed = new List<string>();
        var missing = new List<string>();

        foreach (var (name, _, relativePath) in Entries)
        {
            var targetDir = Path.Combine(_modelsPath, relativePath);
            if (IsComponentInstalled(name, targetDir))
                installed.Add(name);
            else
                missing.Add(name);
        }

        var status = new AIModelsSetupStatus { Installed = installed, Missing = missing };
        return Task.FromResult(status);
    }

    /// <summary>
    /// Server is installed if llama-server.exe exists; other components if the directory exists and has files.
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
        var totalSteps = toInstall.Length;

        for (var i = 0; i < toInstall.Length; i++)
        {
            var (name, fileName, relativePath) = toInstall[i];
            var url = ReleaseBaseUrl + fileName;
            var targetDir = Path.Combine(_modelsPath, relativePath);

            progress?.Report(new AIModelsSetupProgress
            {
                Progress = (double)i / totalSteps,
                Message = $"Downloading {fileName}..."
            });

            var downloadResult = await DownloadWithRetryAsync(url, progress, (double)i / totalSteps, 1.0 / totalSteps, cancellationToken).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
                return Result<AIModelsSetupResult>.Failure(downloadResult.ErrorMessage!, downloadResult.Exception);

            progress?.Report(new AIModelsSetupProgress
            {
                Progress = (double)(i + 1) / totalSteps - 0.01,
                Message = $"Extracting {fileName}..."
            });

            try
            {
                ExtractZipToDirectory(downloadResult.Value!, targetDir);
            }
            catch (Exception ex)
            {
                return Result<AIModelsSetupResult>.Failure($"Failed to extract {fileName}: {ex.Message}", ex);
            }
            finally
            {
                try { File.Delete(downloadResult.Value!); } catch { /* ignore */ }
            }

            installed.Add(name);
        }

        progress?.Report(new AIModelsSetupProgress { Progress = 1.0, Message = "Refreshing model registry..." });

        await _modelRegistry.RefreshAsync().ConfigureAwait(false);

        var serverPath = await _settingsService.GetAsync<string>("AI.LlamaCpp.ServerPath").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            var defaultPath = Path.Combine(_modelsPath, "llamaServer", "llama-server.exe");
            await _settingsService.SetAsync("AI.LlamaCpp.ServerPath", defaultPath).ConfigureAwait(false);
        }

        return Result<AIModelsSetupResult>.Success(new AIModelsSetupResult { Installed = installed });
    }

    private async Task<Result<string>> DownloadWithRetryAsync(
        string url,
        IProgress<AIModelsSetupProgress>? progress,
        double progressStart,
        double progressSpan,
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
                        if (total > 0)
                        {
                            var p = progressStart + progressSpan * (read / (double)total);
                            progress?.Report(new AIModelsSetupProgress { Progress = p, Message = null });
                        }
                    }
                }
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
