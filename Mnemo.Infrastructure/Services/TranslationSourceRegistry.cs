using System.Collections.Generic;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Mutable registry of translation sources. Used at startup (and optionally at runtime for extensions)
/// before <see cref="LocalizationService"/> is constructed.
/// </summary>
public class TranslationSourceRegistry : ITranslationSourceRegistry
{
    private readonly List<ITranslationSource> _sources = new();

    /// <summary>
    /// Gets the list of registered sources. Later sources override earlier ones for the same key.
    /// </summary>
    public IReadOnlyList<ITranslationSource> Sources => _sources;

    /// <inheritdoc />
    public void Add(ITranslationSource source)
    {
        _sources.Add(source);
    }
}
