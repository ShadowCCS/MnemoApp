using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Provides localization services for the application.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string for the specified key and namespace.
    /// </summary>
    /// <param name="key">The key of the string to retrieve.</param>
    /// <param name="ns">The namespace (optional).</param>
    /// <returns>The localized string.</returns>
    string GetString(string key, string? ns = null);

    /// <summary>
    /// Shorthand for GetString.
    /// </summary>
    /// <param name="key">The key of the string to retrieve.</param>
    /// <param name="ns">The namespace (optional).</param>
    /// <returns>The localized string.</returns>
    string T(string key, string? ns = null);

    /// <summary>
    /// Sets the current application language asynchronously.
    /// </summary>
    /// <param name="languageCode">The ISO language code (e.g. "en", "es").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the language was set successfully; false if no translations could be loaded.</returns>
    Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of available languages.
    /// </summary>
    /// <returns>A collection of available language manifests.</returns>
    Task<IEnumerable<Mnemo.Core.Models.LanguageManifest>> GetAvailableLanguagesAsync();

    /// <summary>
    /// Gets the current language code.
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Occurs when the language is changed.
    /// </summary>
    event EventHandler? LanguageChanged;
}
