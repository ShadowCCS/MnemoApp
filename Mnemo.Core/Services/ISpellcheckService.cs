using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Spellcheck provider for local editor text.
/// </summary>
public interface ISpellcheckService
{
    Task<IReadOnlyList<SpellcheckIssue>> CheckAsync(
        IReadOnlyList<InlineSpan> spans,
        IReadOnlyList<string> languageCodes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SuggestAsync(
        string word,
        IReadOnlyList<string> languageCodes,
        CancellationToken cancellationToken);

    Task AddWordAsync(
        string word,
        IReadOnlyList<string> languageCodes,
        CancellationToken cancellationToken);
}
