using System;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI.PlatformHardware;

/// <summary>DRM sysfs (<c>/sys/class/drm</c>) and NVIDIA procfs (<c>/proc/driver/nvidia</c>).</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHardwareGpuProvider : IPlatformHardwareGpuProvider
{
    private static readonly Regex CardDirRegex = new(@"^card\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NvidiaVramMiB = new(@"VRAM\s*:\s*(\d+)\s*MiB", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ILoggerService _logger;
    private readonly object _cacheLock = new();
    private HardwareGpuSnapshot? _cachedSnapshot;

    public LinuxHardwareGpuProvider(ILoggerService logger)
    {
        _logger = logger;
    }

    public HardwareGpuSnapshot GetGpuSnapshot()
    {
        lock (_cacheLock)
        {
            if (_cachedSnapshot != null)
                return _cachedSnapshot;
        }

        long maxVram = 0;
        var hasNvidia = false;
        var hasAmd = false;

        try
        {
            TryNvidiaProcDriver(ref maxVram, ref hasNvidia);
            TryDrmSysfs(ref maxVram, ref hasNvidia, ref hasAmd);
        }
        catch (Exception ex)
        {
            _logger.Warning("LinuxHardwareGpuProvider", $"GPU detection failed: {ex.Message}");
        }

        var snapshot = new HardwareGpuSnapshot(hasNvidia, hasAmd, Math.Max(0, maxVram));
        lock (_cacheLock)
        {
            if (_cachedSnapshot != null)
                return _cachedSnapshot;
            _cachedSnapshot = snapshot;
            return _cachedSnapshot;
        }
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedSnapshot = null;
        }
    }

    private static void TryNvidiaProcDriver(ref long maxVram, ref bool hasNvidia)
    {
        const string root = "/proc/driver/nvidia/gpus";
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var gpuDir in Directory.EnumerateDirectories(root))
        {
            var infoPath = Path.Combine(gpuDir, "information");
            if (!File.Exists(infoPath))
            {
                continue;
            }

            hasNvidia = true;
            var text = File.ReadAllText(infoPath);
            var m = NvidiaVramMiB.Match(text);
            if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mib))
            {
                var bytes = mib * 1024L * 1024L;
                if (bytes > maxVram)
                {
                    maxVram = bytes;
                }
            }
        }
    }

    private static void TryDrmSysfs(ref long maxVram, ref bool hasNvidia, ref bool hasAmd)
    {
        const string drmRoot = "/sys/class/drm";
        if (!Directory.Exists(drmRoot))
        {
            return;
        }

        foreach (var path in Directory.EnumerateDirectories(drmRoot))
        {
            var name = Path.GetFileName(path);
            if (name == null || !CardDirRegex.IsMatch(name))
            {
                continue;
            }

            var devicePath = Path.Combine(path, "device");
            if (!Directory.Exists(devicePath))
            {
                continue;
            }

            var vendorPath = Path.Combine(devicePath, "vendor");
            if (File.Exists(vendorPath))
            {
                var v = File.ReadAllText(vendorPath).Trim();
                if (v.Equals("0x10de", StringComparison.OrdinalIgnoreCase))
                {
                    hasNvidia = true;
                }

                if (v.Equals("0x1002", StringComparison.OrdinalIgnoreCase) || v.Equals("0x1022", StringComparison.OrdinalIgnoreCase))
                {
                    hasAmd = true;
                }
            }

            var memPath = Path.Combine(devicePath, "mem_info_vram_total");
            if (File.Exists(memPath) && long.TryParse(File.ReadAllText(memPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            {
                if (bytes > maxVram)
                {
                    maxVram = bytes;
                }
            }
        }
    }
}
