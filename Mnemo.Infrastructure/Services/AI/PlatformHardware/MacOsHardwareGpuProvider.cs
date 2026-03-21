using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI.PlatformHardware;

/// <summary>Parses <c>system_profiler SPDisplaysDataType</c> output (JSON when available).</summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsHardwareGpuProvider : IPlatformHardwareGpuProvider
{
    private static readonly Regex VramTextRegex = new(
        @"(\d+(?:\.\d+)?)\s*(GB|MB|KB)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ILoggerService _logger;

    public MacOsHardwareGpuProvider(ILoggerService logger)
    {
        _logger = logger;
    }

    public HardwareGpuSnapshot GetGpuSnapshot()
    {
        long vram = 0;
        var hasNvidia = false;
        var hasAmd = false;

        try
        {
            var json = RunSystemProfiler(json: true);
            if (!string.IsNullOrEmpty(json))
            {
                (vram, hasNvidia, hasAmd) = ParseProfilerJson(json);
            }

            if (!hasNvidia && !hasAmd || vram <= 0)
            {
                var text = RunSystemProfiler(json: false);
                if (!string.IsNullOrEmpty(text))
                {
                    MergeTextHeuristics(text, ref vram, ref hasNvidia, ref hasAmd);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("MacOsHardwareGpuProvider", $"GPU detection failed: {ex.Message}");
        }

        return new HardwareGpuSnapshot(hasNvidia, hasAmd, Math.Max(0, vram));
    }

    private static string? RunSystemProfiler(bool json)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/sbin/system_profiler",
            Arguments = json ? "SPDisplaysDataType -json" : "SPDisplaysDataType",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null)
        {
            return null;
        }

        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        return p.ExitCode == 0 ? output : null;
    }

    private static (long Vram, bool Nvidia, bool Amd) ParseProfilerJson(string json)
    {
        long maxVram = 0;
        var nvidia = false;
        var amd = false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("SPDisplaysDataType", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return (0, false, false);
            }

            foreach (var display in arr.EnumerateArray())
            {
                if (display.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (display.TryGetProperty("spdisplays_vendor", out var vendorEl))
                {
                    var vs = vendorEl.GetString() ?? string.Empty;
                    if (vs.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        nvidia = true;
                    }

                    if (vs.Contains("AMD", StringComparison.OrdinalIgnoreCase) || vs.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        amd = true;
                    }
                }

                if (display.TryGetProperty("spdisplays_vram", out var vramEl))
                {
                    var s = vramEl.ValueKind == JsonValueKind.String ? vramEl.GetString() : vramEl.ToString();
                    var b = ParseVramString(s);
                    if (b > maxVram)
                    {
                        maxVram = b;
                    }
                }

                foreach (var prop in display.EnumerateObject())
                {
                    if (!prop.Name.Contains("vram", StringComparison.OrdinalIgnoreCase) &&
                        !prop.Name.Contains("memory", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var s = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    var b = ParseVramString(s);
                    if (b > maxVram)
                    {
                        maxVram = b;
                    }
                }
            }
        }
        catch
        {
            // Caller may fall back to plain-text profiler output
        }

        return (maxVram, nvidia, amd);
    }

    private static void MergeTextHeuristics(string text, ref long maxVram, ref bool nvidia, ref bool amd)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("nvidia", StringComparison.Ordinal) || lower.Contains("geforce", StringComparison.Ordinal))
        {
            nvidia = true;
        }

        if (lower.Contains("amd", StringComparison.Ordinal) || lower.Contains("radeon", StringComparison.Ordinal))
        {
            amd = true;
        }

        foreach (Match m in VramTextRegex.Matches(text))
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                continue;
            }

            var unit = m.Groups[2].Value.ToUpperInvariant();
            long bytes = unit switch
            {
                "KB" => (long)(val * 1024),
                "MB" => (long)(val * 1024 * 1024),
                "GB" => (long)(val * 1024 * 1024 * 1024),
                _ => 0
            };
            if (bytes > maxVram)
            {
                maxVram = bytes;
            }
        }
    }

    private static long ParseVramString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return 0;
        }

        var m = VramTextRegex.Match(s);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
        {
            return 0;
        }

        var unit = m.Groups[2].Value.ToUpperInvariant();
        return unit switch
        {
            "KB" => (long)(val * 1024),
            "MB" => (long)(val * 1024 * 1024),
            "GB" => (long)(val * 1024 * 1024 * 1024),
            _ => 0
        };
    }
}
