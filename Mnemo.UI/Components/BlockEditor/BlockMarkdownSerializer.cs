using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mnemo.Core.Models;
using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Serializes and deserializes block content to/from markdown for clipboard and cross-block selection.
/// </summary>
public static class BlockMarkdownSerializer
{
    /// <summary>
    /// Serializes the given blocks to a markdown string (one block per line or multi-line for code).
    /// </summary>
    public static string Serialize(IEnumerable<BlockViewModel> blocks)
    {
        var sb = new System.Text.StringBuilder();
        var list = blocks.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (i > 0 && !IsMultilineBlock(b.Type))
                sb.AppendLine();
            sb.Append(SerializeBlock(b));
            if (IsMultilineBlock(b.Type))
                sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Serializes a single block to a markdown line (or multi-line for code).
    /// </summary>
    public static string SerializeBlock(BlockViewModel block)
    {
        var body = SerializeBodyForMarkdown(block);
        return block.Type switch
        {
            BlockType.Text => body,
            BlockType.Heading1 => $"# {body}",
            BlockType.Heading2 => $"## {body}",
            BlockType.Heading3 => $"### {body}",
            BlockType.Heading4 => $"#### {body}",
            BlockType.BulletList => $"- {body}",
            BlockType.NumberedList => $"{block.ListNumberIndex}. {body}",
            BlockType.Checklist => block.IsChecked ? $"- [x] {body}" : $"- [ ] {body}",
            BlockType.Quote => "> " + body.Replace("\n", "\n> ", StringComparison.Ordinal),
            BlockType.Code => SerializeCodeFence(block),
            BlockType.Divider => "---",
            BlockType.Image => SerializeImageMarkdown(block),
            BlockType.Equation => "$$\n" + block.EquationLatex + "\n$$",
            BlockType.Page => "[[" + "page:" + block.ReferenceNoteId + "]]",
            _ => body
        };
    }

    private static string SerializeImageMarkdown(BlockViewModel block)
    {
        var path = block.ImagePath ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var alt = (block.Content ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        return $"![{alt}]({path})";
    }

    private static string SerializeCodeFence(BlockViewModel block)
    {
        var lang = (block.CodeLanguage ?? string.Empty).Trim();
        var body = block.Content ?? string.Empty;
        return string.IsNullOrEmpty(lang)
            ? "```\n" + body + "\n```"
            : "```" + lang + "\n" + body + "\n```";
    }

    /// <summary>Inline markdown for rich blocks; code/divider use literal <see cref="BlockViewModel.Content"/> in <see cref="SerializeBlock"/>.</summary>
    public static string SerializeBodyForMarkdown(BlockViewModel block)
    {
        if (block.Type is BlockType.Code or BlockType.Divider)
            return block.Content ?? string.Empty;
        return BlockEditorContentPolicy.WithoutLegacySentinel(InlineMarkdownSerializer.SerializeSpans(block.Spans));
    }

    /// <summary>
    /// Deserializes a markdown string into block view models. Uses BlockFactory for creation.
    /// </summary>
    public static BlockViewModel[] Deserialize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<BlockViewModel>();

        var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new List<BlockViewModel>();
        int order = 0;
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Divider
            if (trimmed == "---" || line.Trim() == "---")
            {
                result.Add(BlockFactory.CreateBlock(BlockType.Divider, order++));
                i++;
                continue;
            }

            var pageMd = Regex.Match(trimmed, @"^\[\[page:([^\]]*)\]\]\s*$");
            if (pageMd.Success)
            {
                var vm = BlockFactory.CreateBlock(BlockType.Page, order++);
                vm.ReferenceNoteId = pageMd.Groups[1].Value.Trim();
                result.Add(vm);
                i++;
                continue;
            }

            // Equation block: $$ fence (multiline or single-line)
            if (trimmed == "$$" || (trimmed.StartsWith("$$") && trimmed.EndsWith("$$") && trimmed.Length > 2))
            {
                if (trimmed == "$$")
                {
                    var eqContent = new System.Text.StringBuilder();
                    i++;
                    while (i < lines.Length)
                    {
                        if (lines[i].TrimStart() == "$$") { i++; break; }
                        if (eqContent.Length > 0) eqContent.AppendLine();
                        eqContent.Append(lines[i]);
                        i++;
                    }
                    var eqBlock = BlockFactory.CreateBlock(BlockType.Equation, order++);
                    eqBlock.EquationLatex = eqContent.ToString().Trim();
                    result.Add(eqBlock);
                }
                else
                {
                    var inner = trimmed[2..^2].Trim();
                    var eqBlock = BlockFactory.CreateBlock(BlockType.Equation, order++);
                    eqBlock.EquationLatex = inner;
                    result.Add(eqBlock);
                    i++;
                }
                continue;
            }

            // Code block (multiline)
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var fenceLang = trimmed.Length > 3 ? trimmed[3..].Trim() : string.Empty;
                var language = string.IsNullOrEmpty(fenceLang) ? "csharp" : fenceLang;
                var codeContent = new System.Text.StringBuilder();
                i++;
                while (i < lines.Length)
                {
                    var codeLine = lines[i];
                    if (codeLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        i++;
                        break;
                    }
                    if (codeContent.Length > 0) codeContent.AppendLine();
                    codeContent.Append(codeLine);
                    i++;
                }
                var codeBlock = BlockFactory.CreateBlock(BlockType.Code, order++);
                codeBlock.CodeLanguage = language;
                codeBlock.Content = codeContent.ToString();
                result.Add(codeBlock);
                continue;
            }

            // Headings (must check #### before ### before ## before #)
            if (trimmed.StartsWith("#### ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.Heading4, trimmed["#### ".Length..].Trim(), order++));
                i++;
                continue;
            }
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.Heading3, trimmed["### ".Length..].Trim(), order++));
                i++;
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.Heading2, trimmed["## ".Length..].Trim(), order++));
                i++;
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.Heading1, trimmed["# ".Length..].Trim(), order++));
                i++;
                continue;
            }

            // Checklist: - [ ] or - [x]
            if (Regex.IsMatch(trimmed, @"^-\s*\[\s*[xX]\s*\]"))
            {
                var content = Regex.Replace(trimmed, @"^-\s*\[\s*[xX]\s*\]\s*", "", RegexOptions.None).Trim();
                var vm = CreateBlockWithContent(BlockType.Checklist, content, order++);
                vm.IsChecked = true;
                result.Add(vm);
                i++;
                continue;
            }
            if (Regex.IsMatch(trimmed, @"^-\s*\[\s*\]"))
            {
                var content = Regex.Replace(trimmed, @"^-\s*\[\s*\]\s*", "", RegexOptions.None).Trim();
                result.Add(CreateBlockWithContent(BlockType.Checklist, content, order++));
                i++;
                continue;
            }

            // Bullet list: - at start (after optional leading whitespace on the line)
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.BulletList, trimmed["- ".Length..].Trim(), order++));
                i++;
                continue;
            }

            // Bullet list: * or + (CommonMark); require whitespace after marker so "*emphasis*" is not a list
            var starOrPlusBullet = Regex.Match(trimmed, @"^(\*|\+)\s+(.*)$");
            if (starOrPlusBullet.Success)
            {
                result.Add(CreateBlockWithContent(BlockType.BulletList, starOrPlusBullet.Groups[2].Value.Trim(), order++));
                i++;
                continue;
            }

            // Quote
            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                var firstLine = trimmed == ">" ? string.Empty : trimmed["> ".Length..].Trim();
                var quoteLines = new List<string> { firstLine };
                i++;
                while (i < lines.Length)
                {
                    var nextTrimmed = lines[i].TrimStart();
                    if (nextTrimmed.StartsWith("> ", StringComparison.Ordinal))
                    {
                        quoteLines.Add(nextTrimmed["> ".Length..].Trim());
                        i++;
                    }
                    else if (nextTrimmed == ">")
                    {
                        quoteLines.Add(string.Empty);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }
                result.Add(CreateBlockWithContent(BlockType.Quote, string.Join("\n", quoteLines), order++));
                continue;
            }

            // Numbered list: digit(s) + .
            if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
            {
                var content = Regex.Replace(trimmed, @"^\d+\.\s*", "", RegexOptions.None).Trim();
                result.Add(CreateBlockWithContent(BlockType.NumberedList, content, order++));
                i++;
                continue;
            }

            // Markdown image ![alt](path)
            var imgMatch = Regex.Match(trimmed, @"^!\[([^\]]*)\]\(([^)]+)\)\s*$");
            if (imgMatch.Success)
            {
                var vm = BlockFactory.CreateBlock(BlockType.Image, order++);
                vm.ImagePath = UnescapeMarkdownImageTarget(imgMatch.Groups[2].Value.Trim());
                vm.ImageWidth = 0;
                var alt = UnescapeMarkdownImageAlt(imgMatch.Groups[1].Value);
                vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(alt) });
                result.Add(vm);
                i++;
                continue;
            }

            // Plain text
            result.Add(CreateBlockWithContent(BlockType.Text, line, order++));
            i++;
        }

        return result.ToArray();
    }

    private static BlockViewModel CreateBlockWithContent(BlockType type, string content, int order)
    {
        var vm = BlockFactory.CreateBlock(type, order);
        if (type == BlockType.Divider)
            return vm;
        vm.SetSpans(InlineMarkdownParser.ToSpans(content ?? string.Empty));
        return vm;
    }

    private static bool IsMultilineBlock(BlockType type) => type is BlockType.Code or BlockType.Equation;

    private static string UnescapeMarkdownImageAlt(string alt) =>
        alt.Replace("\\]", "]", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string UnescapeMarkdownImageTarget(string raw)
    {
        var t = raw.Trim();
        if (t.Length >= 2 && t[0] == '<' && t[^1] == '>')
            t = t[1..^1].Trim();
        return t;
    }
}
