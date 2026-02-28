namespace Mnemo.Core.Services;

/// <summary>
/// Registry for translation sources. Used at startup (and optionally at runtime for extensions)
/// to collect sources before <see cref="ILocalizationService"/> is built.
/// </summary>
public interface ITranslationSourceRegistry
{
    /// <summary>
    /// Adds a translation source. Order matters: later sources override earlier ones for the same key.
    /// </summary>
    /// <param name="source">The source to add.</param>
    void Add(ITranslationSource source);
}
