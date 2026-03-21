using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>DXGI (WDDM) enumeration for dedicated VRAM and PCI vendor IDs.</summary>
[SupportedOSPlatform("windows")]
internal static class DxgiInterop
{
    private const uint VendorIdNvidia = 0x10de;
    private const uint VendorIdAmd = 0x1002;
    private const uint VendorIdAti = 0x1022;
    private const uint VendorIdMicrosoft = 0x1414;

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(IntPtr thisPtr, ref DXGI_ADAPTER_DESC desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr thisPtr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
    }

    internal readonly struct DxgiGpuInfo
    {
        public long MaxDedicatedVideoMemoryBytes { get; init; }
        public bool HasNvidia { get; init; }
        public bool HasAmd { get; init; }
    }

    internal static DxgiGpuInfo TryGetGpuInfo()
    {
        Guid factoryId = IID_IDXGIFactory1;
        int hr = CreateDXGIFactory1(ref factoryId, out IntPtr factory);
        if (hr != 0 || factory == IntPtr.Zero)
        {
            return default;
        }

        try
        {
            IntPtr factoryVtable = Marshal.ReadIntPtr(factory);
            IntPtr enumAdapters1Ptr = Marshal.ReadIntPtr(IntPtr.Add(factoryVtable, IntPtr.Size * 11));
            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(enumAdapters1Ptr);

            long maxNvidia = 0;
            long maxAmd = 0;
            long maxOther = 0;
            var hasNvidia = false;
            var hasAmd = false;

            for (uint i = 0; ; i++)
            {
                hr = enumAdapters1(factory, i, out IntPtr adapter);
                if (hr == DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }

                if (hr != 0 || adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    IntPtr adapterVtable = Marshal.ReadIntPtr(adapter);
                    IntPtr getDescPtr = Marshal.ReadIntPtr(IntPtr.Add(adapterVtable, IntPtr.Size * 6));
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(getDescPtr);

                    var desc = new DXGI_ADAPTER_DESC();
                    hr = getDesc(adapter, ref desc);
                    if (hr == 0)
                    {
                        if (IsSoftwareAdapter(ref desc))
                        {
                            continue;
                        }

                        if (desc.VendorId == VendorIdNvidia)
                        {
                            hasNvidia = true;
                        }

                        if (desc.VendorId == VendorIdAmd || desc.VendorId == VendorIdAti)
                        {
                            hasAmd = true;
                        }

                        long dedicated = (long)desc.DedicatedVideoMemory;
                        if (desc.VendorId == VendorIdNvidia)
                        {
                            if (dedicated > maxNvidia)
                            {
                                maxNvidia = dedicated;
                            }
                        }
                        else if (desc.VendorId == VendorIdAmd || desc.VendorId == VendorIdAti)
                        {
                            if (dedicated > maxAmd)
                            {
                                maxAmd = dedicated;
                            }
                        }
                        else
                        {
                            if (dedicated > maxOther)
                            {
                                maxOther = dedicated;
                            }
                        }
                    }
                }
                finally
                {
                    ReleaseCom(adapter);
                }
            }

            // If a discrete NVIDIA/AMD adapter exists but DedicatedVideoMemory is 0 (common on hybrid),
            // do not fall back to iGPU "dedicated" — leave 0 for WMI/NVML to fill.
            long maxBytes;
            if (maxNvidia > 0)
            {
                maxBytes = maxNvidia;
            }
            else if (maxAmd > 0)
            {
                maxBytes = maxAmd;
            }
            else if (hasNvidia || hasAmd)
            {
                maxBytes = 0;
            }
            else
            {
                maxBytes = maxOther;
            }

            return new DxgiGpuInfo
            {
                MaxDedicatedVideoMemoryBytes = maxBytes,
                HasNvidia = hasNvidia,
                HasAmd = hasAmd
            };
        }
        finally
        {
            ReleaseCom(factory);
        }
    }

    private static bool IsSoftwareAdapter(ref DXGI_ADAPTER_DESC desc)
    {
        if (desc.VendorId == VendorIdMicrosoft)
        {
            return true;
        }

        var d = desc.Description ?? string.Empty;
        if (d.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void ReleaseCom(IntPtr p)
    {
        if (p == IntPtr.Zero)
        {
            return;
        }

        IntPtr vtable = Marshal.ReadIntPtr(p);
        IntPtr releasePtr = Marshal.ReadIntPtr(IntPtr.Add(vtable, IntPtr.Size * 2));
        var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
        release(p);
    }
}
