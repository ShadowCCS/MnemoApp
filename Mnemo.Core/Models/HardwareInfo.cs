namespace Mnemo.Core.Models;

/// <summary>
/// Snapshot of host hardware used for model tier selection and server options.
/// </summary>
public sealed class HardwareInfo
{
    public bool HasNvidiaGpu { get; init; }
    public bool HasAmdGpu { get; init; }
    public long TotalVramBytes { get; init; }
    public int CpuCores { get; init; }
    public bool Is64Bit { get; init; }
    public bool SupportsAvx2 { get; init; } = true;
}
