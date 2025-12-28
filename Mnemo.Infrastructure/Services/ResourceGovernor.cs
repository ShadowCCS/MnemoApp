using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class ResourceGovernor : IResourceGovernor
{
    private readonly ILoggerService _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastAccess = new();
    private readonly SemaphoreSlim _heavyModelLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _loadedModels = new();
    private readonly TimeSpan _unloadTimeout = TimeSpan.FromMinutes(5);
    private readonly Timer _cleanupTimer;

    public event Action<string>? ModelShouldUnload;

    public ResourceGovernor(ILoggerService logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupIdleModels, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<bool> AcquireModelAsync(AIModelManifest manifest, CancellationToken ct)
    {
        _lastAccess[manifest.Id] = DateTime.UtcNow;

        if (manifest.Type == AIModelType.Text && !manifest.IsOptional)
        {
            // Fast models are always considered accessible/loaded
            return true;
        }

        // Heavy models (Smart, TTS, STT) require a lock to prevent VRAM explosion
        if (manifest.EstimatedVramUsageBytes > 500 * 1024 * 1024) // > 500MB
        {
            await _heavyModelLock.WaitAsync(ct).ConfigureAwait(false);
            _loadedModels.TryAdd(manifest.Id, true);
            _logger.Info("ResourceGovernor", $"Acquired heavy model: {manifest.DisplayName}");
            return true;
        }

        return true;
    }

    public void ReleaseModel(AIModelManifest manifest)
    {
        if (manifest.EstimatedVramUsageBytes > 500 * 1024 * 1024)
        {
            if (_loadedModels.TryRemove(manifest.Id, out _))
            {
                _heavyModelLock.Release();
                _logger.Info("ResourceGovernor", $"Released heavy model: {manifest.DisplayName}");
            }
            else
            {
                _logger.Warning("ResourceGovernor", $"Attempted to release model {manifest.DisplayName} that was not acquired");
            }
        }
    }

    private void CleanupIdleModels(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _lastAccess.ToList())
        {
            if (now - kvp.Value > _unloadTimeout)
            {
                if (_loadedModels.TryRemove(kvp.Key, out _))
                {
                    _logger.Info("ResourceGovernor", $"Model {kvp.Key} idle for too long, marking for unload.");
                    ModelShouldUnload?.Invoke(kvp.Key);
                    _lastAccess.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _heavyModelLock.Dispose();
    }
}
