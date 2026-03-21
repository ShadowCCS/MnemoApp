using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;

namespace Mnemo.Infrastructure.Services;

public class ResourceGovernor : IResourceGovernor
{
    private readonly ILoggerService _logger;
    private readonly ISettingsService _settings;
    private readonly IAIModelRegistry _modelRegistry;
    private readonly ConcurrentDictionary<string, DateTime> _lastAccess = new();
    private readonly SemaphoreSlim _heavyModelLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _loadedModels = new();
    private readonly object _idlePolicyLock = new();
    private TimeSpan? _idleUnloadAfter;
    private readonly Timer _cleanupTimer;

    public event Action<string>? ModelShouldUnload;

    public ResourceGovernor(ILoggerService logger, ISettingsService settings, IAIModelRegistry modelRegistry)
    {
        _logger = logger;
        _settings = settings;
        _modelRegistry = modelRegistry;
        _cleanupTimer = new Timer(CleanupIdleModels, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        ReloadIdleUnloadPolicy();
        _settings.SettingChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == "AI.UnloadTimeout")
            ReloadIdleUnloadPolicy();
    }

    private void ReloadIdleUnloadPolicy()
    {
        try
        {
            var raw = _settings.GetAsync("AI.UnloadTimeout", "FifteenMinutes").GetAwaiter().GetResult();
            lock (_idlePolicyLock)
            {
                _idleUnloadAfter = UnloadTimeoutPolicy.ParseToIdleSpanOrNull(raw);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("ResourceGovernor", $"Could not read AI.UnloadTimeout: {ex.Message}");
        }
    }

    public async Task<bool> AcquireModelAsync(AIModelManifest manifest, CancellationToken ct)
    {
        _lastAccess[manifest.Id] = DateTime.UtcNow;

        // Mid/high tier text models are treated as exclusive (one at a time) to avoid VRAM spikes; layout role encodes tier.
        if (RequiresExclusiveResourceSlot(manifest))
        {
            await _heavyModelLock.WaitAsync(ct).ConfigureAwait(false);
            _loadedModels.TryAdd(manifest.Id, true);
            _logger.Info("ResourceGovernor", $"Acquired exclusive-slot model: {manifest.DisplayName}");
            return true;
        }

        return true;
    }

    public void ReleaseModel(AIModelManifest manifest)
    {
        if (RequiresExclusiveResourceSlot(manifest))
        {
            if (_loadedModels.TryRemove(manifest.Id, out _))
            {
                _lastAccess[manifest.Id] = DateTime.UtcNow;
                _heavyModelLock.Release();
                _logger.Info("ResourceGovernor", $"Released exclusive-slot model: {manifest.DisplayName}");
            }
            else
            {
                _logger.Warning("ResourceGovernor", $"Attempted to release model {manifest.DisplayName} that was not acquired");
            }
        }
        else
        {
            _lastAccess[manifest.Id] = DateTime.UtcNow;
        }
    }

    private void CleanupIdleModels(object? state)
    {
        TimeSpan? idleUnloadAfter;
        lock (_idlePolicyLock)
        {
            idleUnloadAfter = _idleUnloadAfter;
        }

        if (idleUnloadAfter == null)
            return;

        var now = DateTime.UtcNow;
        foreach (var kvp in _lastAccess.ToList())
        {
            var manifest = _modelRegistry.GetModelAsync(kvp.Key).GetAwaiter().GetResult();
            if (manifest == null)
                continue;

            if (manifest.Role == AIModelRoles.Manager)
                continue;

            var threshold = UnloadTimeoutPolicy.TierAdjustedIdle(
                idleUnloadAfter.Value,
                manifest.Type == AIModelType.Text && manifest.Role is AIModelRoles.Mid or AIModelRoles.High);

            if (now - kvp.Value <= threshold)
                continue;

            // Never unload a model that is currently acquired/in use
            if (_loadedModels.ContainsKey(kvp.Key))
            {
                continue;
            }

            _logger.Info("ResourceGovernor", $"Model {kvp.Key} idle for too long, marking for unload.");
            ModelShouldUnload?.Invoke(kvp.Key);
            _lastAccess.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingsChanged;
        _cleanupTimer.Dispose();
        _heavyModelLock.Dispose();
    }

    private static bool RequiresExclusiveResourceSlot(AIModelManifest manifest) =>
        manifest.Type == AIModelType.Text &&
        manifest.Role is AIModelRoles.Mid or AIModelRoles.High;
}
