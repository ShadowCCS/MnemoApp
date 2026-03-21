using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Maps detected hardware to a coarse tier for model selection (reusable from onboarding, routing, etc.).
/// </summary>
public interface IHardwareTierEvaluator
{
    HardwarePerformanceTier EvaluateTier(HardwareInfo hardwareInfo);
}
