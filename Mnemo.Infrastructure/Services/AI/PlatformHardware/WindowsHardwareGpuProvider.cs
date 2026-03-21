using System;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;

namespace Mnemo.Infrastructure.Services.AI.PlatformHardware;

/// <summary>DXGI for VRAM and PCI vendor IDs; WMI supplements vendor names and VRAM when DXGI is unavailable.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareGpuProvider : IPlatformHardwareGpuProvider
{
    private readonly ILoggerService _logger;

    public WindowsHardwareGpuProvider(ILoggerService logger)
    {
        _logger = logger;
    }

    public HardwareGpuSnapshot GetGpuSnapshot()
    {
        var dxgi = DxgiInterop.TryGetGpuInfo();
        var hasNvidia = dxgi.HasNvidia;
        var hasAmd = dxgi.HasAmd;
        var vram = dxgi.MaxDedicatedVideoMemoryBytes;

        if (!hasNvidia && !hasAmd)
        {
            MergeWmiVendorFlags(ref hasNvidia, ref hasAmd);
        }

        var wmi = TryWmiVramPreferNvidia();
        var nvml = NvmlInterop.TryGetMaxTotalVramBytes();
        vram = Math.Max(vram, Math.Max(wmi, nvml));

        return new HardwareGpuSnapshot(hasNvidia, hasAmd, Math.Max(0, vram));
    }

    private void MergeWmiVendorFlags(ref bool hasNvidia, ref bool hasAmd)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                var name = mo["Name"]?.ToString()?.ToLowerInvariant() ?? "";
                if (name.Contains("nvidia", StringComparison.Ordinal) || name.Contains("geforce", StringComparison.Ordinal) ||
                    name.Contains("quadro", StringComparison.Ordinal) || name.Contains("tesla", StringComparison.Ordinal))
                {
                    hasNvidia = true;
                }

                if (name.Contains("amd", StringComparison.Ordinal) || name.Contains("radeon", StringComparison.Ordinal))
                {
                    hasAmd = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("WindowsHardwareGpuProvider", $"WMI video controller query failed: {ex.Message}");
        }
    }

    private long TryWmiVramPreferNvidia()
    {
        long nvidiaMax = 0;
        long anyMax = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                var name = mo["Name"]?.ToString()?.ToLowerInvariant() ?? "";
                if (!long.TryParse(mo["AdapterRAM"]?.ToString(), out var ram))
                {
                    continue;
                }

                if (ram is <= 0 or unchecked((long)uint.MaxValue))
                {
                    continue;
                }

                if (ram > anyMax)
                {
                    anyMax = ram;
                }

                if (name.Contains("nvidia", StringComparison.Ordinal) || name.Contains("geforce", StringComparison.Ordinal) ||
                    name.Contains("quadro", StringComparison.Ordinal) || name.Contains("tesla", StringComparison.Ordinal))
                {
                    if (ram > nvidiaMax)
                    {
                        nvidiaMax = ram;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("WindowsHardwareGpuProvider", $"WMI AdapterRAM query failed: {ex.Message}");
        }

        return nvidiaMax > 0 ? nvidiaMax : anyMax;
    }
}
