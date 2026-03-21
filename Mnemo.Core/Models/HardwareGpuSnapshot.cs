namespace Mnemo.Core.Models;

/// <summary>
/// GPU capabilities reported by a platform-specific provider (VRAM and discrete vendor hints).
/// </summary>
public sealed record HardwareGpuSnapshot(bool HasNvidiaGpu, bool HasAmdGpu, long TotalDedicatedVideoMemoryBytes);
