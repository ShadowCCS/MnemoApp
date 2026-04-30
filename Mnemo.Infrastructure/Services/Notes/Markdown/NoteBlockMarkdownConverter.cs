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
            if (i > 0 && b.Type is not BlockType.Code and not BlockType.Equation)
                sb.AppendLine();
            sb.Append(SerializeBlock(b));
            if (b.Type is BlockType.Code or BlockType.Equation)
                sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string SerializeBlock(Block block)
    {
        block.EnsureSpans();
        var body = block.Type is BlockType.Code or BlockType.Divider or BlockType.Equation
            ? block.Content
            : InlineMarkdownSerializer.SerializeSpans(block.Spans);
        var listNum = GetListNumber(block);
        var isChecked = GetChecklistChecked(block);
        return block.Type switch
        {
            BlockType.Text => body,
            BlockType.Heading1 => $"# {body}",
            BlockType.Heading2 => $"## {body}",
            BlockType.Heading3 => $"### {body}",
            BlockType.Heading4 => $"#### {body}",
            BlockType.BulletList => $"- {body}",
            BlockType.NumberedList => $"{listNum}. {body}",
            BlockType.Checklist => isChecked ? $"- [x] {body}" : $"- [ ] {body}",
            BlockType.Quote => "> " + body.Replace("\n", "\n> ", StringComparison.Ordinal),
            BlockType.Code => SerializeCodeFence(block),
            BlockType.Divider => "---",
            BlockType.Equation => "$$\n" + GetEquationLatex(block) + "\n$$",
            BlockType.TwoColumn => block.Children is { Count: >= 2 }
                ? SerializeBlock(block.Children[0]) + "\n\n---\n\n" + SerializeBlock(block.Children[1])
                : string.Empty,
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
                    var eqBlock = new Block
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = BlockType.Equation,
                        Order = order++
                    };
                    var latex = eqContent.ToString().Trim();
                    eqBlock.Payload = new EquationPayload(latex);
                    eqBlock.Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
                    result.Add(eqBlock);
                }
                else
                {
                    var inner = trimmed[2..^2].Trim();
                    var eqBlock = new Block
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = BlockType.Equation,
                        Order = order++
                    };
                    eqBlock.Payload = new EquationPayload(inner);
                    eqBlock.Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
                    result.Add(eqBlock);
                    i++;
                }
                continue;
            }

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

                var source = codeContent.ToString();
                var codeBlock = new Block
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = BlockType.Code,
                    Order = order++,
                    Spans = new List<InlineSpan> { new TextSpan(source, TextStyle.Default) },
                    Payload = new CodePayload(language, source),
                    Meta = new Dictionary<string, object>()
                };
                result.Add(codeBlock);
                continue;
            }

            if (trimmed.StartsWith("#### ", StringComparison.Ordinal))
            {
                result.Add(CreateRichBlock(BlockType.Heading4, trimmed["#### ".Length..].Trim(), order++));
                i++;
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
                b.Payload = new ChecklistPayload(true);
                result.Add(b);
                i++;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^-\s*\[\s*\]"))
            {
                var content = Regex.Replace(trimmed, @"^-\s*\[\s*\]\s*", "", RegexOptions.None).Trim();
                var b = CreateRichBlock(BlockType.Checklist, content, order++);
                b.Payload = new ChecklistPayload(false);
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
        b.Spans = InlineMarkdownParser.ToSpans(content ?? string.Empty);
        if (type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 or BlockType.Heading4)
            EnsureHeadingBold(b);
        return b;
    }

    private static void EnsureHeadingBold(Block b)
    {
        b.EnsureSpans();
        var list = new List<InlineSpan>();
        foreach (var s in b.Spans)
        {
            if (s is TextSpan t)
                list.Add(t with { Style = t.Style.WithSet(InlineFormatKind.Bold) });
            else
                list.Add(s);
        }

        b.Spans = InlineSpanFormatApplier.Normalize(list);
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
        if (block.Payload is ChecklistPayload cp)
            return cp.Checked;
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

    private static string SerializeCodeFence(Block block)
    {
        string source;
        string lang;
        if (block.Payload is CodePayload cp)
        {
            lang = (cp.Language ?? string.Empty).Trim();
            source = cp.Source ?? string.Empty;
        }
        else
        {
            lang = string.Empty;
            source = block.Content ?? string.Empty;
        }

        return string.IsNullOrEmpty(lang)
            ? "```\n" + source + "\n```"
            : "```" + lang + "\n" + source + "\n```";
    }

    private static string GetEquationLatex(Block block)
    {
        if (block.Payload is EquationPayload ep)
            return ep.Latex;
        if (!block.Meta.TryGetValue("equationLatex", out var v) || v == null)
            return string.Empty;
        return v switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString() ?? string.Empty,
            _ => v.ToString() ?? string.Empty
        };
    }
}

