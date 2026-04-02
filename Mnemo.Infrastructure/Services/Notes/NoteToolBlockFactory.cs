using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools.Notes;
using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.Infrastructure.Services.Notes;

internal static class NoteToolBlockFactory
{
    public static Block FromPayload(ToolBlockPayload p, int order)
    {
        if (!Enum.TryParse<BlockType>(p.Type, true, out var bt))
            bt = BlockType.Text;

        var id = string.IsNullOrWhiteSpace(p.BlockId) ? Guid.NewGuid().ToString() : p.BlockId.Trim();
        var b = new Block { Id = id, Type = bt, Order = order };

        if (p.Meta != null)
        {
            foreach (var kv in p.Meta)
                b.Meta[kv.Key] = kv.Value;
        }

        if (bt == BlockType.Divider)
            return b;

        if (bt == BlockType.Code)
        {
            b.Content = p.Content ?? string.Empty;
            return b;
        }

        b.InlineRuns = InlineMarkdownParser.ToRuns(p.Content ?? string.Empty);
        if (bt is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            EnsureHeadingBold(b);

        return b;
    }

    public static List<Block> FromPayloads(IReadOnlyList<ToolBlockPayload> payloads, int startOrder = 0)
    {
        var list = new List<Block>(payloads.Count);
        for (var i = 0; i < payloads.Count; i++)
            list.Add(FromPayload(payloads[i], startOrder + i));
        return list;
    }

    private static void EnsureHeadingBold(Block b)
    {
        b.EnsureInlineRuns();
        var boldRuns = b.InlineRuns!.Select(r => new InlineRun(r.Text, r.Style.WithSet(InlineFormatKind.Bold))).ToList();
        b.InlineRuns = InlineRunFormatApplier.Normalize(boldRuns);
    }
}
