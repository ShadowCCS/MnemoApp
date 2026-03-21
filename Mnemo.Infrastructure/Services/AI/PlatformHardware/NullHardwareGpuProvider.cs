using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI.PlatformHardware;

/// <summary>Used for unknown OS builds or when no specialized provider is available.</summary>
public sealed class NullHardwareGpuProvider : IPlatformHardwareGpuProvider
{
    public HardwareGpuSnapshot GetGpuSnapshot() =>
        new(false, false, 0);

    public void InvalidateCache()
    {
    }
}
