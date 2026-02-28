using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Provides translations for a given culture. Used by <see cref="ILocalizationService"/> to merge
/// strings from built-in, module, and extension sources.
/// </summary>
public interface ITranslationSource
{
    /// <summary>
    /// Gets all translations for the specified culture.
    /// </summary>
    /// <param name="cultureCode">The ISO culture code (e.g. "en", "es").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Namespace -> (key -> value). Return empty dictionary if culture is not available.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetTranslationsForCultureAsync(
        string cultureCode,
        CancellationToken cancellationToken = default);
}
