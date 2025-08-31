using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Service for app-wide localization with per-language JSON dictionaries and namespaced keys.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Current language code, e.g., "en", "de".
        /// </summary>
        string CurrentLanguage { get; }

        /// <summary>
        /// Event raised when language changes. UI can re-evaluate translations.
        /// </summary>
        event EventHandler<string>? LanguageChanged;

        /// <summary>
        /// Returns list of available languages detected from core and registered extensions.
        /// </summary>
        Task<IReadOnlyList<LanguageManifest>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Load language preference from settings and apply, falling back to "en".
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets current language and persists to settings.
        /// </summary>
        Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Translate a key within a namespace. Falls back to English then key.
        /// </summary>
        string T(string @namespace, string key, IReadOnlyDictionary<string, string>? parameters = null);

        /// <summary>
        /// Try get manifest for current language.
        /// </summary>
        LanguageManifest? GetCurrentLanguageManifest();

        /// <summary>
        /// Register or merge a namespaced dictionary for a given language (extensions call this).
        /// </summary>
        void RegisterNamespace(string languageCode, string @namespace, IReadOnlyDictionary<string, string> entries);

        /// <summary>
        /// Bulk register via raw JSON content. JSON shape: { "NamespaceA": { "Key": "Value" }, ... }
        /// </summary>
        void RegisterLanguageJson(string languageCode, string jsonContent);
    }
}


