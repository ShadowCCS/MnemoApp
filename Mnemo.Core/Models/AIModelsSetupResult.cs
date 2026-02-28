using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Result of downloading and extracting all AI model zips.
/// </summary>
public class AIModelsSetupResult
{
    /// <summary>Names of items that were installed (e.g. "router", "fast", "bge-small", "server").</summary>
    public IReadOnlyList<string> Installed { get; init; } = [];
}
