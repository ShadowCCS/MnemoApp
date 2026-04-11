using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        if (bt == BlockType.Equation)
        {
            var latex = p.Content ?? string.Empty;
            b.Payload = new EquationPayload(latex);
            b.Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) };
            b.Meta.Remove("equationLatex");
            return b;
        }

        if (bt == BlockType.Code)
        {
            var lang = b.Meta.TryGetValue("language", out var lv) ? lv?.ToString() ?? "csharp" : "csharp";
            var src = p.Content ?? string.Empty;
            b.Payload = new CodePayload(lang, src);
            b.Spans = new List<InlineSpan> { InlineSpan.Plain(src) };
            b.Meta.Remove("language");
            return b;
        }

        b.Spans = InlineMarkdownParser.ToSpans(p.Content ?? string.Empty);
        if (bt is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3)
            EnsureHeadingBold(b);

        if (bt == BlockType.Checklist)
        {
            b.Payload = new ChecklistPayload(ReadMetaChecked(b.Meta));
            b.Meta.Remove("checked");
        }

        return b;
    }

    private static bool ReadMetaChecked(Dictionary<string, object> meta)
    {
        if (!meta.TryGetValue("checked", out var v) || v == null)
            return false;
        if (v is bool b)
            return b;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.True;
        return false;
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
}
