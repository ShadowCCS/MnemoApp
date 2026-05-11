using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Mnemo.UI.Services;

/// <summary>
/// Maps TextMate scope segments to theme <see cref="IBrush"/> resources (<c>SyntaxCode*Brush</c>).
/// </summary>
public static class SyntaxCodeBrushResolver
{
    /// <summary>Resolve a foreground brush for the innermost meaningful TextMate scope chain.</summary>
    public static IBrush Resolve(IReadOnlyList<string> scopes, IBrush defaultForeground)
    {
        if (scopes == null || scopes.Count == 0)
            return TryDefaultBrush() ?? defaultForeground;

        var app = Application.Current;
        if (app == null)
            return defaultForeground;

        var theme = app.ActualThemeVariant;
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var key = MapScopeToBrushKey(scopes[i]);
            if (key != null && app.TryGetResource(key, theme, out var o) && o is IBrush b)
                return b;
        }

        return TryDefaultBrush() ?? defaultForeground;
    }

    private static IBrush? TryDefaultBrush()
    {
        var app = Application.Current;
        if (app == null)
            return null;
        return app.TryGetResource("SyntaxCodeDefaultBrush", app.ActualThemeVariant, out var o) && o is IBrush b
            ? b
            : null;
    }

    /// <summary>First matching key wins (call on scopes from most-specific outward).</summary>
    private static string? MapScopeToBrushKey(string scope)
    {
        if (string.IsNullOrEmpty(scope))
            return null;

        var s = scope.AsSpan();
        // Order: more specific tokens before broad "keyword"/"string" substrings elsewhere.
        if (Contains(s, "comment")) return "SyntaxCodeCommentBrush";
        if (Contains(s, "string.quoted") || Contains(s, "string.interpolated") || Contains(s, "string.template")
            || Contains(s, "string.unquoted") || EndsWithScope(s, "string"))
            return "SyntaxCodeStringBrush";
        if (Contains(s, "constant.numeric") || Contains(s, "constant.language"))
            return "SyntaxCodeNumberBrush";
        if (Contains(s, "entity.name.tag") || Contains(s, "entity.other.attribute-name"))
            return "SyntaxCodeMarkupBrush";
        if (Contains(s, "entity.name.type") || Contains(s, "entity.name.class") || Contains(s, "storage.type"))
            return "SyntaxCodeTypeBrush";
        if (Contains(s, "entity.name.function") || Contains(s, "support.function") || Contains(s, "meta.function-call"))
            return "SyntaxCodeFunctionBrush";
        if (Contains(s, "variable.parameter") || Contains(s, "variable.language") || Contains(s, "variable.other"))
            return "SyntaxCodeVariableBrush";
        if (Contains(s, "keyword") || Contains(s, "storage.modifier"))
            return "SyntaxCodeKeywordBrush";
        if (Contains(s, "operator") || Contains(s, "assignment"))
            return "SyntaxCodeOperatorBrush";
        if (Contains(s, "punctuation"))
            return "SyntaxCodePunctuationBrush";
        if (Contains(s, "invalid") || Contains(s, "deprecated"))
            return "SyntaxCodeInvalidBrush";
        return null;
    }

    private static bool Contains(ReadOnlySpan<char> scope, string needle)
    {
        return scope.IndexOf(needle.AsSpan(), StringComparison.Ordinal) >= 0;
    }

    private static bool EndsWithScope(ReadOnlySpan<char> scope, string tail)
    {
        return scope.EndsWith(tail.AsSpan(), StringComparison.Ordinal)
               || scope.EndsWith(("." + tail).AsSpan(), StringComparison.Ordinal);
    }
}
