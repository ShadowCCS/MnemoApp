namespace Mnemo.Core.Formatting;

/// <summary>
/// Strips optional surrounding <c>$</c> or <c>$$</c> delimiters from user-selected text
/// before storing as canonical LaTeX source. Used by convert-selection flows only.
/// </summary>
public static class EquationLatexNormalizer
{
    /// <summary>
    /// Trims whitespace and removes one layer of <c>$$…$$</c> or <c>$…$</c> wrapping.
    /// Returns the inner LaTeX string (never null).
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();

        if (trimmed.StartsWith("$$") && trimmed.EndsWith("$$") && trimmed.Length > 4)
            return trimmed[2..^2].Trim();

        if (trimmed.StartsWith('$') && trimmed.EndsWith('$') && trimmed.Length > 2)
            return trimmed[1..^1].Trim();

        return trimmed;
    }
}
