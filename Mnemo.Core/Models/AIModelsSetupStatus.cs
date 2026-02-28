using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Status of which AI setup components (embedding, server, router, fast) are already installed.
/// </summary>
public class AIModelsSetupStatus
{
    /// <summary>Names of components that are already installed (e.g. "router", "fast", "bge-small", "server").</summary>
    public IReadOnlyList<string> Installed { get; init; } = [];

    /// <summary>Names of components that are missing and need to be downloaded.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>True when all required components are installed; onboarding can show "finished".</summary>
    public bool AllInstalled => Missing.Count == 0;
}
