namespace Mnemo.Core.Models;

/// <summary>
/// Progress reported during AI models download and extraction.
/// </summary>
public class AIModelsSetupProgress
{
    /// <summary>Overall progress 0.0 to 1.0.</summary>
    public double Progress { get; init; }

    /// <summary>Current step or file name (e.g. downloading or extracting a model zip).</summary>
    public string? Message { get; init; }
}
