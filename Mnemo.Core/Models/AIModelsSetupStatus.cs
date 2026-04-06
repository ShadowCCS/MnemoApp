using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Status of which AI setup components (embedding, server, manager, low-tier text, etc.) are already installed.
/// </summary>
public class AIModelsSetupStatus
{
    /// <summary>Hardware tier used to decide which text bundle (low / mid / high) is required.</summary>
    public HardwarePerformanceTier HardwareTier { get; init; }

    /// <summary>Names of components that are already installed (e.g. "manager", "low", "bge-small", "server").</summary>
    public IReadOnlyList<string> Installed { get; init; } = [];

    /// <summary>Names of tier-required components that are not installed yet (excludes optional vision zips).</summary>
    public IReadOnlyList<string> RequiredMissing { get; init; } = [];

    /// <summary>Optional vision bundle(s) for this tier that are not installed (e.g. low-image on Low tier).</summary>
    public IReadOnlyList<string> OptionalImageMissing { get; init; } = [];

    /// <summary>True when every required component for this machine is present.</summary>
    public bool AllRequiredInstalled => RequiredMissing.Count == 0;
}
