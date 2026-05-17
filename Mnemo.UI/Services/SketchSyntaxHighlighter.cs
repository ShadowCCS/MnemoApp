using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Mnemo.Core.Sketch;

namespace Mnemo.UI.Services;

/// <summary>
/// Syntax coloring for the Mnemo sketch DSL using <see cref="SketchLexer"/> (not TextMate).
/// Identifiers after <c>-&gt;</c> use the default (body) color; other identifiers use the type color.
/// </summary>
public static class SketchSyntaxHighlighter
{
    public static bool IsSketchLanguage(string? languageFenceId) =>
        string.Equals((languageFenceId ?? string.Empty).Trim(), "sketch", StringComparison.OrdinalIgnoreCase);

    public static string GetDisplayLanguageLabel(string? fenceId)
    {
        var id = (fenceId ?? string.Empty).Trim();
        if (id.Length == 0)
            return "Plain text";
        if (IsSketchLanguage(id))
            return "Sketch syntax";

        var pick = CodeLanguageCatalog.GetLanguages()
            .FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return pick?.Label ?? id.ToUpperInvariant();
    }

    public static string BuildLineNumberText(string? code)
    {
        var normalized = (code ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lineCount = 1;
        foreach (var c in normalized)
        {
            if (c == '\n')
                lineCount++;
        }

        return string.Join("\n", Enumerable.Range(1, lineCount));
    }

    public static void ApplyToTextBlock(TextBlock target, string source, IBrush defaultForeground)
    {
        target.Inlines?.Clear();

        if (string.IsNullOrEmpty(source))
        {
            target.Inlines?.Add(new Run(string.Empty) { Foreground = defaultForeground });
            return;
        }

        var lexer = new SketchLexer(source);
        var (tokens, _) = lexer.Lex();
        var ordered = tokens.Where(t => t.Kind != SketchTokenKind.EndOfFile).OrderBy(t => t.Span.Start.Offset).ToArray();
        var offset = 0;
        var pendingEdgeTargetIdentifier = false;

        foreach (var token in ordered)
        {
            if (token.Span.Start.Offset > offset)
            {
                var gapLen = token.Span.Start.Offset - offset;
                AddRun(target, source.AsSpan(offset, gapLen).ToString(), defaultForeground);
            }

            switch (token.Kind)
            {
                case SketchTokenKind.Newline:
                    pendingEdgeTargetIdentifier = false;
                    AddRun(target, token.Text, defaultForeground);
                    break;
                case SketchTokenKind.ArrowDirected:
                case SketchTokenKind.ArrowUndirected:
                case SketchTokenKind.ArrowBidirectional:
                    pendingEdgeTargetIdentifier = true;
                    AddRun(target, token.Text, ResolveBrush(token.Kind, defaultForeground, false));
                    break;
                case SketchTokenKind.Identifier:
                {
                    var isTarget = pendingEdgeTargetIdentifier;
                    if (pendingEdgeTargetIdentifier)
                        pendingEdgeTargetIdentifier = false;
                    AddRun(target, token.Text, ResolveBrush(token.Kind, defaultForeground, isTarget));
                    break;
                }
                default:
                    AddRun(target, token.Text, ResolveBrush(token.Kind, defaultForeground, false));
                    break;
            }

            offset = Math.Max(offset, token.Span.End.Offset);
        }

        if (offset < source.Length)
            AddRun(target, source[offset..], defaultForeground);
    }

    private static void AddRun(TextBlock target, string text, IBrush brush)
    {
        if (string.IsNullOrEmpty(text))
            return;

        target.Inlines!.Add(new Run(text) { Foreground = brush });
    }

    private static IBrush ResolveBrush(SketchTokenKind kind, IBrush defaultForeground, bool identifierIsEdgeTarget)
    {
        return kind switch
        {
            SketchTokenKind.KeywordSketch or SketchTokenKind.KeywordClass or SketchTokenKind.KeywordGroup
            or SketchTokenKind.KeywordEdge => ThemeBrush("SyntaxCodeKeywordBrush", defaultForeground),
            SketchTokenKind.Identifier => identifierIsEdgeTarget
                ? defaultForeground
                : ThemeBrush("SyntaxCodeTypeBrush", defaultForeground),
            SketchTokenKind.String or SketchTokenKind.HexColor => ThemeBrush("SyntaxCodeStringBrush", defaultForeground),
            SketchTokenKind.Number or SketchTokenKind.Boolean => ThemeBrush("SyntaxCodeNumberBrush", defaultForeground),
            SketchTokenKind.Comment => ThemeBrush("SyntaxCodeCommentBrush", defaultForeground),
            SketchTokenKind.ArrowDirected or SketchTokenKind.ArrowUndirected or SketchTokenKind.ArrowBidirectional
                => ThemeBrush("SyntaxCodeOperatorBrush", defaultForeground),
            SketchTokenKind.LeftBracket or SketchTokenKind.RightBracket or SketchTokenKind.LeftParen
            or SketchTokenKind.RightParen or SketchTokenKind.LeftBrace or SketchTokenKind.RightBrace
            or SketchTokenKind.Colon or SketchTokenKind.Comma
                => ThemeBrush("SyntaxCodePunctuationBrush", defaultForeground),
            SketchTokenKind.Invalid => ThemeBrush("SyntaxCodeInvalidBrush", defaultForeground),
            _ => defaultForeground
        };
    }

    private static IBrush ThemeBrush(string key, IBrush fallback)
    {
        var app = Application.Current;
        return app != null && app.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : fallback;
    }
}
