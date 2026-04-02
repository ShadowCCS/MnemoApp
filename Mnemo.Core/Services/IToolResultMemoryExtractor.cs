using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Extracts structured <see cref="ConversationMemoryEntry"/> facts from a tool result.
/// Implementations are rule-based (no LLM call), fast, and registered per module.
/// A composite implementation aggregates all registered extractors.
/// </summary>
public interface IToolResultMemoryExtractor
{
    /// <summary>
    /// Inspects the tool name and its JSON result content and yields zero or more facts.
    /// Returns an empty enumerable for tools this extractor does not handle.
    /// </summary>
    /// <param name="toolName">The tool that was invoked (case-insensitive).</param>
    /// <param name="resultJson">The formatted JSON string returned by <see cref="IToolResultFormatter"/>.</param>
    /// <param name="turnNumber">Current 1-based turn number in the conversation.</param>
    IEnumerable<ConversationMemoryEntry> Extract(string toolName, string resultJson, int turnNumber);
}
