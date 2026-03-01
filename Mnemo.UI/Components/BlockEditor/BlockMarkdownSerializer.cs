using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mnemo.Core.Models;

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
            if (i > 0 && !IsCodeBlock(b.Type))
                sb.AppendLine();
            sb.Append(SerializeBlock(b));
            if (IsCodeBlock(b.Type))
                sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Serializes a single block to a markdown line (or multi-line for code).
    /// </summary>
    public static string SerializeBlock(BlockViewModel block)
    {
        var content = block.Content ?? string.Empty;
        return block.Type switch
        {
            BlockType.Text => content,
            BlockType.Heading1 => $"# {content}",
            BlockType.Heading2 => $"## {content}",
            BlockType.Heading3 => $"### {content}",
            BlockType.BulletList => $"- {content}",
            BlockType.NumberedList => $"{block.ListNumberIndex}. {content}",
            BlockType.Checklist => block.IsChecked ? $"- [x] {content}" : $"- [ ] {content}",
            BlockType.Quote => "> " + content.Replace("\n", "\n> ", StringComparison.Ordinal),
            BlockType.Code => "```\n" + content + "\n```",
            BlockType.Divider => "---",
            _ => content
        };
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

            // Code block (multiline)
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var codeContent = new System.Text.StringBuilder();
                var openLine = trimmed;
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
                codeBlock.Content = codeContent.ToString();
                result.Add(codeBlock);
                continue;
            }

            // Headings (must check ### before ## before #)
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

            // Bullet list: - at start (after optional whitespace)
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                result.Add(CreateBlockWithContent(BlockType.BulletList, trimmed["- ".Length..].Trim(), order++));
                i++;
                continue;
            }

            // Quote
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                var quoteLines = new List<string> { trimmed["> ".Length..].Trim() };
                i++;
                while (i < lines.Length && lines[i].TrimStart().StartsWith("> ", StringComparison.Ordinal))
                {
                    quoteLines.Add(lines[i].TrimStart()["> ".Length..].Trim());
                    i++;
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

            // Plain text
            result.Add(CreateBlockWithContent(BlockType.Text, line, order++));
            i++;
        }

        return result.ToArray();
    }

    private static BlockViewModel CreateBlockWithContent(BlockType type, string content, int order)
    {
        var vm = BlockFactory.CreateBlock(type, order);
        vm.Content = content ?? string.Empty;
        return vm;
    }

    private static bool IsCodeBlock(BlockType type) => type == BlockType.Code;
}
