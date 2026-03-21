using System;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>Aggregates CPU/OS facts with <see cref="IPlatformHardwareGpuProvider"/> for <see cref="HardwareInfo"/>.</summary>
public sealed class HardwareDetector
{
    private readonly ILoggerService _logger;
    private readonly IPlatformHardwareGpuProvider _platformGpu;
    private readonly object _lock = new();
    private HardwareInfo? _cached;

    /// <summary>Raised when <see cref="Refresh"/> clears the hardware snapshot (GPU list and aggregated <see cref="HardwareInfo"/>).</summary>
    public event Action? SnapshotInvalidated;

    public HardwareDetector(ILoggerService logger, IPlatformHardwareGpuProvider platformGpu)
    {
        _logger = logger;
        _platformGpu = platformGpu;
    }

    /// <summary>Loads and caches hardware once (call from app startup). Subsequent <see cref="Detect"/> calls reuse the snapshot.</summary>
    public HardwareInfo Initialize() => Detect();

    /// <summary>Clears the cache so the next <see cref="Detect"/> re-queries the OS (e.g. before model downloads).</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            _cached = null;
        }

        _platformGpu.InvalidateCache();
        SnapshotInvalidated?.Invoke();
    }

    /// <summary>Returns the cached <see cref="HardwareInfo"/> when available; otherwise samples the OS once and logs.</summary>
    public HardwareInfo Detect()
    {
        lock (_lock)
        {
            if (_cached != null)
            {
                return _cached;
            }

            var gpu = _platformGpu.GetGpuSnapshot();
            var info = new HardwareInfo
            {
                HasNvidiaGpu = gpu.HasNvidiaGpu,
                HasAmdGpu = gpu.HasAmdGpu,
                TotalVramBytes = gpu.TotalDedicatedVideoMemoryBytes,
                CpuCores = Environment.ProcessorCount,
                Is64Bit = Environment.Is64BitOperatingSystem
            };

            _cached = info;

            _logger.Info(
                "HardwareDetector",
                $"Detected Hardware: GPU: {(info.HasNvidiaGpu ? "NVIDIA" : info.HasAmdGpu ? "AMD" : "None")}, VRAM: {info.TotalVramBytes / 1024 / 1024}MB, CPU Cores: {info.CpuCores}");

            return info;
        }
    }
}
