namespace Mnemo.UI.Components.BlockEditor;

/// <summary>Enter key behavior for quote blocks: newline vs exit to new block on an empty line.</summary>
internal static class QuoteEnterBehavior
{
    /// <summary>True if the caret sits on a line that is empty or whitespace-only.</summary>
    public static bool IsCaretOnWhitespaceOnlyLine(string text, int caretIndex)
    {
        GetLineBounds(text, caretIndex, out var lineStart, out var lineEndExclusive);
        var line = text[lineStart..lineEndExclusive];
        return string.IsNullOrWhiteSpace(line);
    }

    /// <summary>
    /// When the caret is on a whitespace-only line, computes quote body and text for the following block.
    /// </summary>
    public static bool TryGetSplitOnEmptyLineEnter(string text, int caretIndex, out string quoteBody, out string followingBlockText)
    {
        quoteBody = followingBlockText = string.Empty;
        caretIndex = System.Math.Clamp(caretIndex, 0, text.Length);
        GetLineBounds(text, caretIndex, out var lineStart, out var lineEndExclusive);

        var line = text[lineStart..lineEndExclusive];
        if (!string.IsNullOrWhiteSpace(line))
            return false;

        quoteBody = text[..lineStart].TrimEnd('\r', '\n');

        var tailStart = lineEndExclusive;
        if (tailStart < text.Length)
        {
            if (text[tailStart] == '\r' && tailStart + 1 < text.Length && text[tailStart + 1] == '\n')
                tailStart += 2;
            else if (text[tailStart] == '\n' || text[tailStart] == '\r')
                tailStart += 1;
        }

        followingBlockText = text[tailStart..].TrimStart('\r', '\n');
        return true;
    }

    private static void GetLineBounds(string text, int caretIndex, out int lineStart, out int lineEndExclusive)
    {
        lineStart = caretIndex == 0 ? 0 : text.LastIndexOf('\n', caretIndex - 1) + 1;
        lineEndExclusive = text.IndexOf('\n', lineStart);
        if (lineEndExclusive < 0)
            lineEndExclusive = text.Length;
    }
}
