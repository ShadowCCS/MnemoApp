using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Heuristic tier from VRAM and GPU presence. Tune thresholds when onboarding reports real capability.
/// </summary>
public sealed class HardwareTierEvaluator : IHardwareTierEvaluator
{
    private const long FourGbBytes = 4L * 1024 * 1024 * 1024;
    private const long EightGbBytes = 8L * 1024 * 1024 * 1024;

    public HardwarePerformanceTier EvaluateTier(HardwareInfo hardwareInfo)
    {
        var hasDiscreteGpu = hardwareInfo.HasNvidiaGpu || hardwareInfo.HasAmdGpu;
        if (!hasDiscreteGpu)
            return HardwarePerformanceTier.Low;

        var vram = hardwareInfo.TotalVramBytes;
        if (vram > 0 && vram < FourGbBytes)
            return HardwarePerformanceTier.Low;

        if (vram >= EightGbBytes)
            return HardwarePerformanceTier.High;

        if (vram == 0)
            return HardwarePerformanceTier.Mid;

        return HardwarePerformanceTier.Mid;
    }
}
