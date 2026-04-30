namespace Mnemo.Core.Services;

/// <summary>
/// Persistent user dictionary words by language code.
/// </summary>
public interface IUserSpellbookService
{
    Task<IReadOnlySet<string>> GetWordsAsync(string languageCode, CancellationToken cancellationToken);
    Task AddWordAsync(string languageCode, string word, CancellationToken cancellationToken);
}
