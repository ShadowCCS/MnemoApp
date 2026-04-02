using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>
/// Aggregates all registered <see cref="IToolResultMemoryExtractor"/> implementations.
/// Each module registers its own extractor; this composite dispatches to all of them
/// and merges the results.
/// </summary>
public sealed class CompositeToolResultMemoryExtractor : IToolResultMemoryExtractor
{
    private readonly IReadOnlyList<IToolResultMemoryExtractor> _extractors;

    public CompositeToolResultMemoryExtractor(IEnumerable<IToolResultMemoryExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public IEnumerable<ConversationMemoryEntry> Extract(string toolName, string resultJson, int turnNumber)
    {
        foreach (var extractor in _extractors)
        {
            foreach (var fact in extractor.Extract(toolName, resultJson, turnNumber))
                yield return fact;
        }
    }
}
