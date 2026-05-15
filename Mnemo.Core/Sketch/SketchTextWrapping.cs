using System;
using System.Collections.Generic;

namespace Mnemo.Core.Sketch;

internal static class SketchTextWrapping
{
    public const double ApproximateCharacterWidth = 7.5;
    public const double LineHeight = 18;
    public const int MaxLineCharacters = 28;

    private static readonly char[] SpaceSeparator = [' '];

    public static IReadOnlyList<string> WrapLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return [string.Empty];

        var rawWords = label.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawWords.Length == 0)
            return [string.Empty];

        // Estimate: most words pass through; chunked-word split adds at most label.Length / MaxLineCharacters extras.
        var capacity = rawWords.Length + label.Length / MaxLineCharacters;
        var buffer = new List<string>(capacity);
        foreach (var w in rawWords)
            AppendSplitLongWord(buffer, w);

        if (buffer.Count == 0)
            return [string.Empty];

        // Materialize once so we can use string.Join(char, string[], int, int) which doesn't allocate a sub-array.
        var words = buffer.Count == buffer.Capacity ? buffer.ToArray() : buffer.ToArray();
        var lines = new List<string>(Math.Max(1, words.Length / 4));
        var start = 0;
        var currentLength = words[0].Length;

        for (var i = 1; i < words.Length; i++)
        {
            var candidateLength = currentLength + 1 + words[i].Length; // +1 for joining space
            if (candidateLength <= MaxLineCharacters)
            {
                currentLength = candidateLength;
                continue;
            }

            lines.Add(JoinRange(words, start, i - start));
            start = i;
            currentLength = words[i].Length;
        }

        lines.Add(JoinRange(words, start, words.Length - start));
        return lines;
    }

    public static double MeasureLineWidth(string line) =>
        Math.Max(1, line.Length) * ApproximateCharacterWidth;

    private static string JoinRange(string[] words, int start, int count) =>
        count == 1 ? words[start] : string.Join(' ', words, start, count);

    private static void AppendSplitLongWord(List<string> sink, string word)
    {
        if (word.Length <= MaxLineCharacters)
        {
            sink.Add(word);
            return;
        }

        for (var i = 0; i < word.Length; i += MaxLineCharacters)
            sink.Add(word.Substring(i, Math.Min(MaxLineCharacters, word.Length - i)));
    }
}
