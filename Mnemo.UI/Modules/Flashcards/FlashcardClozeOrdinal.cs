using System.Text.RegularExpressions;

namespace Mnemo.UI.Modules.Flashcards;

internal static class FlashcardClozeOrdinal
{
    private static readonly Regex ClozeHead = new(@"\{\{c(\d+)::", RegexOptions.Compiled);

    /// <summary>Next cloze index for <c>{{cN::…}}</c> markers in plain text.</summary>
    public static int ComputeNext(string text)
    {
        var max = 0;
        foreach (Match m in ClozeHead.Matches(text))
        {
            if (int.TryParse(m.Groups[1].Value, out var n))
                max = Math.Max(max, n);
        }

        return max + 1;
    }
}
