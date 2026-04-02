using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.Notes.Markdown;

/// <summary>
/// Block-model markdown conversion aligned with <c>BlockMarkdownSerializer</c> in the UI (paste semantics).
/// </summary>
public static class NoteBlockMarkdownConverter
{
    public static string Serialize(IReadOnlyList<Block> blocks)
    {
        var ordered = blocks.OrderBy(b => b.Order).ToList();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            if (i > 0 && b.Type != BlockType.Code)
                sb.AppendLine();
            sb.Append(SerializeBlock(b));
            if (b.Type == BlockType.Code)
                sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string SerializeBlock(Block block)
    {
        block.EnsureInlineRuns();
        var body = block.Type is BlockType.Code or BlockType.Divider
            ? block.Content
            : InlineMarkdownSerializer.SerializeRuns(block.InlineRuns!);
        var listNum = GetListNumber(block);
        var isChecked = GetChecklistChecked(block);
        return block.Type switch
        {
            BlockType.Text => body,
            BlockType.Heading1 => $"# {body}",
            BlockType.Heading2 => $"## {body}",
            BlockType.Heading3 => $"### {body}",
            BlockType.BulletList => $"- {body}",
            BlockType.NumberedList => $"{listNum}. {body}",
            BlockType.Checklist => isChecked ? $"- [x] {body}" : $"- [ ] {body}",
            BlockType.Quote => "> " + body.Replace("\n", "\n> ", StringComparison.Ordinal),
            BlockType.Code => "```\n" + (block.Content ?? string.Empty) + "\n```",
            BlockType.Divider => "---",
            _ => body
        };
    }

    public static List<Block> Deserialize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new List<Block>();
        var order = 0;
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed == "---" || line.Trim() == "---")
            {
                result.Add(CreateDivider(order++));
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
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

                var codeBlock = new Block
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = BlockType.Code,
                    Order = order++,
                    Content = codeContent.ToString()
                };
                result.Add(codeBlock);
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                result.Add(CreateRichBlock(BlockType.Heading3, trimmed["### ".Length..].Trim(), order++));
                i++;
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                result.Add(CreateRichBlock(BlockType.Heading2, trimmed["## ".Length..].Trim(), order++));
                i++;
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                result.Add(CreateRichBlock(BlockType.Heading1, trimmed["# ".Length..].Trim(), order++));
                i++;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^-\s*\[\s*[xX]\s*\]"))
            {
                var content = Regex.Replace(trimmed, @"^-\s*\[\s*[xX]\s*\]\s*", "", RegexOptions.None).Trim();
                var b = CreateRichBlock(BlockType.Checklist, content, order++);
                b.Meta["checked"] = true;
                result.Add(b);
                i++;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^-\s*\[\s*\]"))
            {
                var content = Regex.Replace(trimmed, @"^-\s*\[\s*\]\s*", "", RegexOptions.None).Trim();
                var b = CreateRichBlock(BlockType.Checklist, content, order++);
                b.Meta["checked"] = false;
                result.Add(b);
                i++;
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                result.Add(CreateRichBlock(BlockType.BulletList, trimmed["- ".Length..].Trim(), order++));
                i++;
                continue;
            }

            var starOrPlusBullet = Regex.Match(trimmed, @"^(\*|\+)\s+(.*)$");
            if (starOrPlusBullet.Success)
            {
                result.Add(CreateRichBlock(BlockType.BulletList, starOrPlusBullet.Groups[2].Value.Trim(), order++));
                i++;
                continue;
            }

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

                result.Add(CreateRichBlock(BlockType.Quote, string.Join("\n", quoteLines), order++));
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
            {
                var content = Regex.Replace(trimmed, @"^\d+\.\s*", "", RegexOptions.None).Trim();
                var m = Regex.Match(trimmed, @"^(\d+)\.\s");
                var n = m.Success && int.TryParse(m.Groups[1].Value, out var num) ? num : 1;
                var nb = CreateRichBlock(BlockType.NumberedList, content, order++);
                nb.Meta["listNumber"] = n;
                result.Add(nb);
                i++;
                continue;
            }

            result.Add(CreateRichBlock(BlockType.Text, line, order++));
            i++;
        }

        return result;
    }

    private static Block CreateDivider(int order) =>
        new() { Id = Guid.NewGuid().ToString(), Type = BlockType.Divider, Order = order };

    private static Block CreateRichBlock(BlockType type, string content, int order)
    {
        var b = new Block
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Order = order
        };
        if (type == BlockType.Divider)
            return b;
        b.InlineRuns = InlineMarkdownParser.ToRuns(content ?? string.Empty);
        if (type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            EnsureHeadingBold(b);
        return b;
    }

    private static void EnsureHeadingBold(Block b)
    {
        b.EnsureInlineRuns();
        var boldRuns = b.InlineRuns!.Select(r => new InlineRun(r.Text, r.Style.WithSet(InlineFormatKind.Bold))).ToList();
        b.InlineRuns = InlineRunFormatApplier.Normalize(boldRuns);
    }

    private static int GetListNumber(Block block)
    {
        if (!block.Meta.TryGetValue("listNumber", out var v))
            return 1;
        return v switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je when je.TryGetInt32(out var n) => n,
            _ => 1
        };
    }

    private static bool GetChecklistChecked(Block block)
    {
        if (!block.Meta.TryGetValue("checked", out var v))
            return false;
        return v switch
        {
            bool b => b,
            JsonElement je when je.ValueKind is JsonValueKind.True => true,
            JsonElement je when je.ValueKind is JsonValueKind.False => false,
            _ => false
        };
    }
}

