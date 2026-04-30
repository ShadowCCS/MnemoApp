using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Spellcheck;

public sealed class SpellDictionaryCatalogService : ISpellDictionaryCatalogService
{
    public bool TryResolve(string languageCode, out string affPath, out string dicPath)
    {
        affPath = string.Empty;
        dicPath = string.Empty;

        var code = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrEmpty(code))
            return false;

        var baseDirectory = AppContext.BaseDirectory;
        var dictionariesDirectory = Path.Combine(baseDirectory, "Dictionaries", "Spellcheck");

        foreach (var candidate in GetCandidates(code))
        {
            if (TryResolveCandidate(dictionariesDirectory, candidate, out var candidateAff, out var candidateDic))
            {
                affPath = candidateAff;
                dicPath = candidateDic;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetCandidates(string normalizedCode)
    {
        yield return normalizedCode;
        var primary = normalizedCode.Split('-')[0];
        if (!string.Equals(primary, normalizedCode, StringComparison.Ordinal))
            yield return primary;
    }

    private static bool TryResolveCandidate(
        string dictionariesDirectory,
        string candidate,
        out string affPath,
        out string dicPath)
    {
        // Layout A: Dictionaries/Spellcheck/en.aff + en.dic
        affPath = Path.Combine(dictionariesDirectory, candidate + ".aff");
        dicPath = Path.Combine(dictionariesDirectory, candidate + ".dic");
        if (File.Exists(affPath) && File.Exists(dicPath))
            return true;

        // Layout B: Dictionaries/Spellcheck/en/en.aff + en.dic
        var folder = Path.Combine(dictionariesDirectory, candidate);
        affPath = Path.Combine(folder, candidate + ".aff");
        dicPath = Path.Combine(folder, candidate + ".dic");
        if (File.Exists(affPath) && File.Exists(dicPath))
            return true;

        affPath = string.Empty;
        dicPath = string.Empty;
        return false;
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return string.Empty;

        var trimmed = languageCode.Trim().Replace('_', '-');
        var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        if (parts.Length == 1)
            return parts[0].ToLowerInvariant();

        return $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
    }
}
