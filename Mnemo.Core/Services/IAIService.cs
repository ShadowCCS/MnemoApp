using System;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Provides access to AI models and related services.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Subscribes to changes in the currently selected model.
    /// </summary>
    /// <param name="handler">The handler to call when the selection changes.</param>
    void SubscribeToSelectedModelChanges(Action<string?> handler);

    /// <summary>
    /// Unsubscribes from changes in the currently selected model.
    /// </summary>
    /// <param name="handler">The handler to remove.</param>
    void UnsubscribeFromSelectedModelChanges(Action<string?> handler);

    /// <summary>
    /// Gets the ID of the currently selected model.
    /// </summary>
    /// <returns>The model ID or null if none selected.</returns>
    string? GetSelectedModel();

    /// <summary>
    /// Counts the tokens in a string for a specific model.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <param name="model">The model ID.</param>
    /// <returns>A result containing success status and token count.</returns>
    Task<TokenCountResult> CountTokensAsync(string text, string model);

    /// <summary>
    /// Gets information about a specific model.
    /// </summary>
    /// <param name="modelId">The model ID.</param>
    /// <returns>Model info or null if not found.</returns>
    Task<AIModelInfo?> GetModelAsync(string modelId);
}

public record TokenCountResult(bool Success, int TokenCount);

public class AIModelInfo
{
    public AIModelCapabilities? Capabilities { get; set; }
}

public class AIModelCapabilities
{
    public int MaxContextLength { get; set; }
}

