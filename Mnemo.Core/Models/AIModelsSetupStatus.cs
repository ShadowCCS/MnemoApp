using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Status of which AI setup components (embedding, server, manager, low-tier text, etc.) are already installed.
/// </summary>
public class AIModelsSetupStatus
{
    /// <summary>Names of components that are already installed (e.g. "manager", "low", "bge-small", "server").</summary>
    public IReadOnlyList<string> Installed { get; init; } = [];

    /// <summary>Names of components that are missing and required for the current machine tier (mid vs high chat + vision bundles per tier).</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>True when all tier-required components are installed; onboarding can show "finished".</summary>
    public bool AllInstalled => Missing.Count == 0;
}
