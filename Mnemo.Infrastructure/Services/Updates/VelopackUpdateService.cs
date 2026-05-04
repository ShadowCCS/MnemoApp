using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using NuGet.Versioning;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Mnemo.Infrastructure.Services.Updates;

public sealed class VelopackUpdateService : IUpdateService, IDisposable
{
    private const string GithubRepoUrl = "https://github.com/onemneo/mnemo";
    private static readonly Uri ReleasesLatestApi = new("https://api.github.com/repos/onemneo/mnemo/releases/latest");

    private readonly ILoggerService _logger;
    private readonly HttpClient _httpClient;
    private UpdateManager? _updateManager;
    private Velopack.UpdateInfo? _pendingVelopackUpdate;
    private AppUpdateInfo? _pendingPortableUpdate;

    public VelopackUpdateService(ILoggerService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MnemoDesktop/1.0 (update-check)");
    }

    public void Dispose() => _httpClient.Dispose();

    private UpdateManager GetOrCreateUpdateManager()
    {
        if (_updateManager != null)
            return _updateManager;

        var source = new GithubSource(GithubRepoUrl, accessToken: null, prerelease: false);
        _updateManager = new UpdateManager(source, new UpdateOptions(), locator: null);
        return _updateManager;
    }

    public bool SupportsInAppApply
    {
        get
        {
            try
            {
                var um = GetOrCreateUpdateManager();
                return um.IsInstalled && !um.IsPortable;
            }
            catch (Exception ex)
            {
                _logger.Warning("Updates", $"SupportsInAppApply probe failed: {ex.Message}");
                return false;
            }
        }
    }

    public string CurrentDisplayVersion
    {
        get
        {
            try
            {
                var um = GetOrCreateUpdateManager();
                if (um.CurrentVersion != null)
                    return um.CurrentVersion.ToString();
            }
            catch
            {
                // fall through
            }

            return ReadInformationalVersionFromEntryAssembly();
        }
    }

    public async Task<Result<AppUpdateInfo?>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        _pendingVelopackUpdate = null;
        _pendingPortableUpdate = null;

        try
        {
            var um = GetOrCreateUpdateManager();
            if (um.IsInstalled && !um.IsPortable)
            {
                try
                {
                    var vp = await um.CheckForUpdatesAsync().ConfigureAwait(false);
                    if (vp == null)
                        return Result<AppUpdateInfo?>.Success(null);

                    _pendingVelopackUpdate = vp;
                    var asset = vp.TargetFullRelease;
                    var info = new AppUpdateInfo(
                        asset.Version.ToString(),
                        asset.NotesMarkdown,
                        publishedAtUtc: null,
                        isMandatory: false);
                    return Result<AppUpdateInfo?>.Success(info);
                }
                catch (NotInstalledException)
                {
                    // Fall through to GitHub API (same as unpackaged / portable).
                }
            }

            return await CheckPortableViaGithubAsync(um, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Updates", $"CheckForUpdatesAsync failed: {ex.Message}");
            return Result<AppUpdateInfo?>.Failure(ex.Message, ex);
        }
    }

    private async Task<Result<AppUpdateInfo?>> CheckPortableViaGithubAsync(UpdateManager um, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesLatestApi);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Result<AppUpdateInfo?>.Failure($"GitHub API {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagEl))
            return Result<AppUpdateInfo?>.Failure("GitHub release has no tag_name.");

        var tag = tagEl.GetString() ?? string.Empty;
        var versionString = tag.TrimStart('v', 'V');
        if (!SemanticVersion.TryParse(versionString, out var remoteVersion))
            return Result<AppUpdateInfo?>.Failure($"Could not parse release tag as semantic version: {tag}");

        var current = ResolveSemanticCurrentVersion(um);
        if (current != null && remoteVersion <= current)
            return Result<AppUpdateInfo?>.Success(null);

        var bodyMd = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
        DateTime? published = null;
        if (root.TryGetProperty("published_at", out var pubEl))
        {
            var s = pubEl.GetString();
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out var dt))
                published = dt;
        }

        var info = new AppUpdateInfo(remoteVersion.ToString(), bodyMd, published, isMandatory: false);
        _pendingPortableUpdate = info;
        return Result<AppUpdateInfo?>.Success(info);
    }

    private SemanticVersion? ResolveSemanticCurrentVersion(UpdateManager um)
    {
        if (um.CurrentVersion != null && SemanticVersion.TryParse(um.CurrentVersion.ToString(), out var vp))
            return vp;

        var s = ReadInformationalVersionFromEntryAssembly();
        if (SemanticVersion.TryParse(s, out var parsed))
            return parsed;

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0 && SemanticVersion.TryParse(s[..plus], out var noMeta))
            return noMeta;

        return null;
    }

    private static string ReadInformationalVersionFromEntryAssembly()
    {
        var asm = Assembly.GetEntryAssembly();
        var attr = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(attr?.InformationalVersion))
            return attr.InformationalVersion;

        return asm?.GetName().Version?.ToString() ?? "0.0.0";
    }

    public async Task<Result> DownloadUpdatesAsync(AppUpdateInfo update, IProgress<int>? progress, CancellationToken cancellationToken = default)
    {
        if (_pendingVelopackUpdate == null)
            return Result.Failure("In-app download is only available for a Velopack-installed build.");

        var asset = _pendingVelopackUpdate.TargetFullRelease;
        if (!string.Equals(asset.Version.ToString(), update.Version, StringComparison.OrdinalIgnoreCase))
            return Result.Failure("Download requested for a different version than the pending Velopack update.");

        try
        {
            var um = GetOrCreateUpdateManager();
            void OnProgress(int p) => progress?.Report(p);
            await um.DownloadUpdatesAsync(_pendingVelopackUpdate, OnProgress, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("Updates", "DownloadUpdatesAsync (Velopack) failed.", ex);
            return Result.Failure(ex.Message, ex);
        }
    }

    public void ApplyUpdatesAndRestart()
    {
        if (_pendingVelopackUpdate == null)
        {
            _logger.Warning("Updates", "ApplyUpdatesAndRestart called with no pending Velopack update.");
            return;
        }

        try
        {
            var um = GetOrCreateUpdateManager();
            um.ApplyUpdatesAndRestart(_pendingVelopackUpdate.TargetFullRelease, restartArgs: Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.Error("Updates", "ApplyUpdatesAndRestart failed.", ex);
        }
    }
}
