using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public class HardwareDetector
{
    private readonly ILoggerService _logger;

    public HardwareDetector(ILoggerService logger)
    {
        _logger = logger;
    }

    public HardwareInfo Detect()
    {
        var info = new HardwareInfo
        {
            HasNvidiaGpu = IsNvidiaGpuPresent(),
            HasAmdGpu = IsAmdGpuPresent(),
            TotalVramBytes = GetTotalVram(),
            CpuCores = Environment.ProcessorCount,
            Is64Bit = Environment.Is64BitOperatingSystem
        };

        _logger.Info("HardwareDetector", $"Detected Hardware: GPU: {(info.HasNvidiaGpu ? "NVIDIA" : info.HasAmdGpu ? "AMD" : "None")}, VRAM: {info.TotalVramBytes / 1024 / 1024}MB, CPU Cores: {info.CpuCores}");
        
        return info;
    }

    private bool IsNvidiaGpuPresent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                var name = mo["Name"]?.ToString()?.ToLowerInvariant() ?? "";
                if (name.Contains("nvidia") || name.Contains("geforce") || name.Contains("quadro") || name.Contains("tesla"))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("HardwareDetector", $"Failed to detect NVIDIA GPU via WMI: {ex.Message}");
        }
        return false;
    }

    private bool IsAmdGpuPresent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                var name = mo["Name"]?.ToString()?.ToLowerInvariant() ?? "";
                if (name.Contains("amd") || name.Contains("radeon"))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("HardwareDetector", $"Failed to detect AMD GPU via WMI: {ex.Message}");
        }
        return false;
    }

    private long GetTotalVram()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return 0;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
            long maxRam = 0;
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                if (long.TryParse(mo["AdapterRAM"]?.ToString(), out var ram))
                {
                    if (ram > maxRam) maxRam = ram;
                }
            }
            return maxRam;
        }
        catch
        {
            return 0;
        }
    }
}

public class HardwareInfo
{
    public bool HasNvidiaGpu { get; set; }
    public bool HasAmdGpu { get; set; }
    public long TotalVramBytes { get; set; }
    public int CpuCores { get; set; }
    public bool Is64Bit { get; set; }
    public bool SupportsAvx2 => true; // Assume true for most modern CPUs
}
