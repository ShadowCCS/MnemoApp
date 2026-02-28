using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Central localization service that merges translations from all registered sources
/// and resolves keys for the current culture.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IEnumerable<ITranslationSource> _sources;
    private readonly ILoggerService _logger;
    private readonly string _defaultCulture;
    private readonly object _lock = new();
    private Dictionary<string, Dictionary<string, string>> _current = new();
    private string _currentLanguage;
    private const string LocalizationCategory = "Localization";

    /// <inheritdoc />
    public string CurrentLanguage => _currentLanguage;

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Creates a new localization service.
    /// </summary>
    /// <param name="sources">Translation sources; later sources override earlier for the same key.</param>
    /// <param name="logger">Logger for errors and diagnostics.</param>
    /// <param name="defaultCulture">Default culture code when a requested culture fails to load (e.g. "en").</param>
    public LocalizationService(
        IEnumerable<ITranslationSource> sources,
        ILoggerService logger,
        string defaultCulture = "en")
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultCulture = defaultCulture ?? throw new ArgumentNullException(nameof(defaultCulture));
        _currentLanguage = defaultCulture;
    }

    /// <inheritdoc />
    public string GetString(string key, string? ns = null) => T(key, ns);

    /// <inheritdoc />
    public string T(string key, string? ns = null)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        Dictionary<string, Dictionary<string, string>> snapshot;
        lock (_lock)
        {
            snapshot = _current;
        }

        if (ns != null)
        {
            if (snapshot.TryGetValue(ns, out var keys) && keys.TryGetValue(key, out var value))
                return value;
            return key;
        }

        foreach (var kv in snapshot)
        {
            if (kv.Value.TryGetValue(key, out var value))
                return value;
        }
        return key;
    }

    /// <inheritdoc />
    public async Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var merged = await LoadAndMergeAsync(languageCode, cancellationToken).ConfigureAwait(false);
        if (merged.Count == 0 && languageCode != _defaultCulture)
        {
            _logger.Warning(LocalizationCategory, $"No translations for '{languageCode}', falling back to '{_defaultCulture}'.");
            merged = await LoadAndMergeAsync(_defaultCulture, cancellationToken).ConfigureAwait(false);
        }

        if (merged.Count == 0)
        {
            _logger.Warning(LocalizationCategory, $"No translations loaded for '{languageCode}' or default.");
            return false;
        }

        lock (_lock)
        {
            _current = merged;
            _currentLanguage = languageCode;
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public Task<IEnumerable<LanguageManifest>> GetAvailableLanguagesAsync()
    {
        // Built-in core language packs; later aggregate from sources or a manifest.
        var list = new List<LanguageManifest>
        {
            new() { Code = "en", Name = "English", NativeName = "English" },
            new() { Code = "es", Name = "Spanish", NativeName = "Español" },
            new() { Code = "de", Name = "German", NativeName = "Deutsch" },
            new() { Code = "ja", Name = "Japanese", NativeName = "日本語" }
        };
        return Task.FromResult<IEnumerable<LanguageManifest>>(list);
    }

    private async Task<Dictionary<string, Dictionary<string, string>>> LoadAndMergeAsync(string cultureCode, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var data = await source.GetTranslationsForCultureAsync(cultureCode, cancellationToken).ConfigureAwait(false);
                if (data == null)
                    continue;
                foreach (var kv in data)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;
                    if (!result.TryGetValue(kv.Key, out var keys))
                    {
                        keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result[kv.Key] = keys;
                    }
                    foreach (var entry in kv.Value)
                    {
                        if (!string.IsNullOrEmpty(entry.Key))
                            keys[entry.Key] = entry.Value ?? entry.Key;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(LocalizationCategory, $"Translation source failed for culture '{cultureCode}'.", ex);
            }
        }
        return result;
    }
}
