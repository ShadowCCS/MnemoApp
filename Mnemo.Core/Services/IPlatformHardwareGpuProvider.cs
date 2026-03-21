using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// OS-specific detection of discrete GPU vendors and dedicated video memory for tiering and defaults.
/// </summary>
public interface IPlatformHardwareGpuProvider
{
    /// <summary>Best-effort snapshot; VRAM may be 0 when the OS does not expose it.</summary>
    HardwareGpuSnapshot GetGpuSnapshot();
}
