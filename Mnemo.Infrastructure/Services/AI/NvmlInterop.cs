using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// NVIDIA Management Library — authoritative total VRAM for NVIDIA GPUs (DXGI/WMI often under-report on hybrid WDDM).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NvmlInterop
{
    private const int NvmlSuccess = 0;

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlInit_v2", ExactSpelling = true)]
    private static extern int NvmlInitV2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlInit", ExactSpelling = true)]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlShutdown", ExactSpelling = true)]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetCount", ExactSpelling = true)]
    private static extern int NvmlDeviceGetCount(out uint deviceCount);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetHandleByIndex", ExactSpelling = true)]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetMemoryInfo", ExactSpelling = true)]
    private static extern int NvmlDeviceGetMemoryInfo(IntPtr device, ref MemoryInfo memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    /// <summary>Returns the largest <c>total</c> memory among enumerated NVIDIA devices, or 0 if NVML is unavailable.</summary>
    internal static long TryGetMaxTotalVramBytes()
    {
        try
        {
            if (!TryNvmlInit())
            {
                return 0;
            }

            try
            {
                if (NvmlDeviceGetCount(out var count) != NvmlSuccess || count == 0)
                {
                    return 0;
                }

                ulong maxTotal = 0;
                for (uint i = 0; i < count; i++)
                {
                    if (NvmlDeviceGetHandleByIndex(i, out var dev) != NvmlSuccess)
                    {
                        continue;
                    }

                    var info = new MemoryInfo();
                    if (NvmlDeviceGetMemoryInfo(dev, ref info) != NvmlSuccess)
                    {
                        continue;
                    }

                    if (info.Total > maxTotal)
                    {
                        maxTotal = info.Total;
                    }
                }

                return maxTotal > long.MaxValue ? long.MaxValue : (long)maxTotal;
            }
            finally
            {
                NvmlShutdown();
            }
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (BadImageFormatException)
        {
            return 0;
        }
    }

    private static bool TryNvmlInit()
    {
        try
        {
            return NvmlInitV2() == NvmlSuccess;
        }
        catch (EntryPointNotFoundException)
        {
            try
            {
                return NvmlInit() == NvmlSuccess;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
