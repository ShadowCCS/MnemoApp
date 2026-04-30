namespace Mnemo.Core.Services;

/// <summary>
/// Resolves Hunspell dictionary paths by language code.
/// </summary>
public interface ISpellDictionaryCatalogService
{
    bool TryResolve(string languageCode, out string affPath, out string dicPath);
}
