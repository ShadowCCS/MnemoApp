using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mnemo.Core.Models.Markdown;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class MarkdownProcessor : IMarkdownProcessor
{
    private static readonly Regex DisplayMathRegex = new(@"\$\$(.+?)\$\$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineMathRegex = new(@"\$(.+?)\$", RegexOptions.Compiled);
    private static readonly Regex HighlightRegex = new(@"==(.+?)==", RegexOptions.Compiled);
    private static readonly Regex StrikethroughRegex = new(@"~~(.+?)~~", RegexOptions.Compiled);
    private static readonly Regex SubscriptRegex = new(@"_(\{[^}]+?\}|\w)", RegexOptions.Compiled);
    private static readonly Regex SuperscriptRegex = new(@"\^(\{[^}]+?\}|\w)", RegexOptions.Compiled);
    private static readonly Regex TildeSubscriptRegex = new(@"~([^~]+)~", RegexOptions.Compiled);
    private static readonly Regex CaretSuperscriptRegex = new(@"\^([^^\s]+)\^", RegexOptions.Compiled);

    public (string ProcessedSource, Dictionary<string, MarkdownSpecialInline> SpecialInlines) ExtractSpecialInlines(string source)
    {
        var inlines = new Dictionary<string, MarkdownSpecialInline>();
        var counter = 0;

        // Extract display math $$...$$ first (must be before inline math)
        var processed = DisplayMathRegex.Replace(source, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.DisplayMath);
            return key;
        });

        // Extract inline math $...$
        processed = InlineMathRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.InlineMath);
            return key;
        });

        // Extract highlighted text ==...==
        processed = HighlightRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.Highlight);
            return key;
        });

        // Extract strikethrough ~~...~~ (MUST be before tilde subscript!)
        processed = StrikethroughRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.Strikethrough);
            return key;
        });

        // Extract caret superscript ^...^ (MUST be before LaTeX superscript!)
        processed = CaretSuperscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.Superscript);
            return key;
        });

        // Extract LaTeX superscript ^{...} or ^x
        processed = SuperscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            if (content.StartsWith("{") && content.EndsWith("}"))
                content = content.Substring(1, content.Length - 2);
            inlines[key] = new MarkdownSpecialInline(content, MarkdownInlineType.Superscript);
            return key;
        });

        // Extract tilde subscript ~...~
        processed = TildeSubscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            inlines[key] = new MarkdownSpecialInline(match.Groups[1].Value, MarkdownInlineType.Subscript);
            return key;
        });

        // Extract LaTeX subscript _{...} or _x
        processed = SubscriptRegex.Replace(processed, match =>
        {
            var key = $"ⓈⓅⒺⒸⒾⒶⓁ{counter++}Ⓢ";
            var content = match.Groups[1].Value;
            if (content.StartsWith("{") && content.EndsWith("}"))
                content = content.Substring(1, content.Length - 2);
            inlines[key] = new MarkdownSpecialInline(content, MarkdownInlineType.Subscript);
            return key;
        });

        return (processed, inlines);
    }
}



