using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

/// <summary>
/// Learns per-installation pause-to-send delay (rolling median) to schedule typing-idle prefetch
/// without relying on a fixed global idle timeout.
/// </summary>
public sealed class ChatPauseToSendEstimator
{
    private const string SettingsKey = "Chat.TypingPause.PauseToSendSamplesMs";
    private const int MaxSamples = 30;
    private const int MinSamplesForPersonalization = 10;
    private const int DefaultTriggerDelayMs = 1250;
    private const int WarmupBudgetMs = 800;
    private const int MinTriggerMs = 500;
    private const int MaxTriggerMs = 2500;

    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private double[] _samples = Array.Empty<double>();
    private bool _loaded;

    public ChatPauseToSendEstimator(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<int> GetIdleTriggerDelayMsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        double[] snapshot;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            snapshot = _samples;
        }
        finally
        {
            _lock.Release();
        }

        if (snapshot.Length < MinSamplesForPersonalization)
            return DefaultTriggerDelayMs;

        var median = Median(snapshot);
        var trigger = (int)Math.Round(median - WarmupBudgetMs);
        return Math.Clamp(trigger, MinTriggerMs, MaxTriggerMs);
    }

    public async Task RecordPauseToSendSampleAsync(double pauseMs, CancellationToken ct = default)
    {
        if (pauseMs < 50 || pauseMs > 120_000)
            return;

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<double>(_samples) { pauseMs };
            while (list.Count > MaxSamples)
                list.RemoveAt(0);
            _samples = list.ToArray();
            await _settings.SetAsync(SettingsKey, _samples).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
            return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
                return;
            var stored = await _settings.GetAsync(SettingsKey, Array.Empty<double>()).ConfigureAwait(false);
            _samples = stored ?? Array.Empty<double>();
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return DefaultTriggerDelayMs;
        var sorted = values.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2;
        return sorted[mid];
    }
}
