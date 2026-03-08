namespace Mnemo.Core.Models;

/// <summary>
/// A single styled text span within a block's content.
/// Immutable — mutations produce new lists of runs.
/// </summary>
public sealed record InlineRun(string Text, InlineStyle Style = default)
{
    public static InlineRun Plain(string text) => new(text, InlineStyle.Default);
}
