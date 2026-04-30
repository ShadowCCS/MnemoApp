using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Spellcheck;

public sealed class UserSpellbookService : IUserSpellbookService
{
    private const string SpellbookKey = "Editor.SpellCheckCustomWordsByLanguage";

    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, HashSet<string>>? _cache;

    public UserSpellbookService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlySet<string>> GetWordsAsync(string languageCode, CancellationToken cancellationToken)
    {
        var code = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrEmpty(code))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (!all.TryGetValue(code, out var words))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddWordAsync(string languageCode, string word, CancellationToken cancellationToken)
    {
        var code = NormalizeLanguageCode(languageCode);
        var normalizedWord = NormalizeWord(word);
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(normalizedWord))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await GetAllUnsafeAsync().ConfigureAwait(false);
            if (!all.TryGetValue(code, out var words))
            {
                words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                all[code] = words;
            }

            if (words.Add(normalizedWord))
                await PersistAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Clone(await GetAllUnsafeAsync().ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> GetAllUnsafeAsync()
    {
        if (_cache != null)
            return _cache;

        var stored = await _settings.GetAsync(SpellbookKey, new Dictionary<string, string[]>()).ConfigureAwait(false);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in stored)
        {
            var code = NormalizeLanguageCode(kv.Key);
            if (string.IsNullOrEmpty(code))
                continue;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in kv.Value ?? [])
            {
                var normalized = NormalizeWord(word);
                if (!string.IsNullOrEmpty(normalized))
                    set.Add(normalized);
            }

            result[code] = set;
        }

        _cache = result;
        return _cache;
    }

    private async Task PersistAsync(Dictionary<string, HashSet<string>> all)
    {
        var serializable = all.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(static w => w, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        await _settings.SetAsync(SpellbookKey, serializable).ConfigureAwait(false);
    }

    private static Dictionary<string, HashSet<string>> Clone(Dictionary<string, HashSet<string>> source)
    {
        var copy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
            copy[kv.Key] = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return string.Empty;
        var normalized = languageCode.Trim().Replace('_', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;
        if (parts.Length == 1)
            return parts[0].ToLowerInvariant();
        return $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
    }

    private static string NormalizeWord(string? word) => (word ?? string.Empty).Trim().ToLowerInvariant();
}
