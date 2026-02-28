using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Loads translations from JSON embedded in a specified assembly. Use for modules and widgets
/// that ship their own Translations/{culture}.json. Schema: { "NamespaceName": { "Key1": "Value1", ... } }.
/// </summary>
public class EmbeddedJsonTranslationSource : ITranslationSource
{
    private readonly Assembly _assembly;
    private readonly string _resourceBaseName;
    private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _cache = new();

    /// <summary>
    /// Creates a new source that reads from the given assembly's embedded resources.
    /// Resource name is <paramref name="resourceBaseName"/> + "." + cultureCode + ".json"
    /// (e.g. "Mnemo.UI.Modules.Overview.Widgets.FlashcardStats.Translations" + ".en.json").
    /// </summary>
    /// <param name="assembly">Assembly containing the embedded JSON resources.</param>
    /// <param name="resourceBaseName">Fully qualified resource base name without culture or extension (e.g. "Mnemo.UI.Modules.Overview.Widgets.FlashcardStats.Translations").</param>
    public EmbeddedJsonTranslationSource(Assembly assembly, string resourceBaseName)
    {
        _assembly = assembly ?? throw new System.ArgumentNullException(nameof(assembly));
        _resourceBaseName = resourceBaseName ?? throw new System.ArgumentNullException(nameof(resourceBaseName));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetTranslationsForCultureAsync(
        string cultureCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cultureCode))
            return new Dictionary<string, IReadOnlyDictionary<string, string>>();

        var cacheKey = _resourceBaseName + "|" + cultureCode;
        lock (_cache)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var resourceName = _resourceBaseName + "." + cultureCode + ".json";
        await using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return new Dictionary<string, IReadOnlyDictionary<string, string>>();

        var dict = await DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        var immutable = ToReadOnly(dict);

        lock (_cache)
        {
            _cache[cacheKey] = immutable;
        }
        return immutable;
    }

    private static async Task<Dictionary<string, Dictionary<string, string>>> DeserializeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var root = await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, string>>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (root == null)
            return new Dictionary<string, Dictionary<string, string>>();
        return root;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ToReadOnly(
        Dictionary<string, Dictionary<string, string>> dict)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(dict.Count);
        foreach (var kv in dict)
        {
            result[kv.Key] = kv.Value != null ? new Dictionary<string, string>(kv.Value) : new Dictionary<string, string>();
        }
        return result;
    }
}
