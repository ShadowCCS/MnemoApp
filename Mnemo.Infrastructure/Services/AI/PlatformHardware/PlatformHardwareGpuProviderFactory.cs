using System;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI.PlatformHardware;

/// <summary>Selects the appropriate <see cref="IPlatformHardwareGpuProvider"/> for the current OS.</summary>
public static class PlatformHardwareGpuProviderFactory
{
    public static IPlatformHardwareGpuProvider Create(ILoggerService logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsHardwareGpuProvider(logger);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxHardwareGpuProvider(logger);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsHardwareGpuProvider(logger);
        }

        return new NullHardwareGpuProvider();
    }
}
