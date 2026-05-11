using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.Core.Sketch;

internal static class SketchTextWrapping
{
    public const double ApproximateCharacterWidth = 7.5;
    public const double LineHeight = 18;
    public const int MaxLineCharacters = 28;

    public static IReadOnlyList<string> WrapLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return [string.Empty];

        var words = label
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitLongWord)
            .ToArray();

        if (words.Length == 0)
            return [string.Empty];

        var lines = new List<string>();
        var current = words[0];

        for (var i = 1; i < words.Length; i++)
        {
            var candidate = current + " " + words[i];
            if (candidate.Length <= MaxLineCharacters)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = words[i];
        }

        lines.Add(current);
        return lines;
    }

    public static double MeasureLineWidth(string line) =>
        Math.Max(1, line.Length) * ApproximateCharacterWidth;

    private static IEnumerable<string> SplitLongWord(string word)
    {
        if (word.Length <= MaxLineCharacters)
            return [word];

        var chunks = new List<string>();
        for (var i = 0; i < word.Length; i += MaxLineCharacters)
            chunks.Add(word.Substring(i, Math.Min(MaxLineCharacters, word.Length - i)));

        return chunks;
    }
}
