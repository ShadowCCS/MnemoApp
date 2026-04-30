namespace Mnemo.Core.Models;

/// <summary>
/// Represents one misspelled token in editor text.
/// </summary>
public sealed record SpellcheckIssue(
    int Start,
    int Length,
    string Word,
    IReadOnlyList<string> Suggestions);
