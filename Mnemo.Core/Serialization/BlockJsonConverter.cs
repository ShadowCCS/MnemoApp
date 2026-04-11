using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.Core.Serialization;

/// <summary>Reads legacy <c>inlineRuns</c> / <c>content</c> and writes canonical <c>spans</c> + <c>payload</c>.</summary>
public sealed class BlockJsonConverter : JsonConverter<Block>
{
    public override Block? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var block = new Block();

        if (root.TryGetProperty("id", out var idEl))
            block.Id = idEl.GetString() ?? block.Id;
        if (root.TryGetProperty("type", out var typeEl) && Enum.TryParse(typeEl.GetString(), true, out BlockType bt))
            block.Type = bt;
        if (root.TryGetProperty("order", out var orderEl))
            block.Order = orderEl.GetInt32();

        if (root.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
            block.Meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaEl.GetRawText(), options) ?? new();

        if (root.TryGetProperty("children", out var chEl) && chEl.ValueKind == JsonValueKind.Array)
            block.Children = JsonSerializer.Deserialize<List<Block>>(chEl.GetRawText(), options);

        string? legacyContent = root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String
            ? contentProp.GetString()
            : null;

        if (root.TryGetProperty("payload", out var payloadEl))
            block.Payload = ReadPayload(payloadEl);
        else
            block.Payload = PayloadFromLegacyMeta(block.Type, block.Meta, legacyContent);

        if (root.TryGetProperty("spans", out var spansEl) && spansEl.ValueKind == JsonValueKind.Array)
            block.Spans = ReadSpans(spansEl);
        else if (root.TryGetProperty("inlineRuns", out var runsEl) && runsEl.ValueKind == JsonValueKind.Array)
            block.Spans = LegacyRunsToSpans(runsEl);
        else if (legacyContent != null)
            block.Spans = new List<InlineSpan> { InlineSpan.Plain(legacyContent) };
        else
            block.Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        if (block.Type == BlockType.Code && block.Payload is CodePayload cp
            && string.IsNullOrEmpty(InlineSpanText.FlattenDisplay(block.Spans).Trim()) && !string.IsNullOrEmpty(cp.Source))
            block.Spans = new List<InlineSpan> { InlineSpan.Plain(cp.Source) };

        if (block.Type == BlockType.Equation)
            block.Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) };

        block.Spans = InlineSpanFormatApplier.Normalize(block.Spans);
        return block;
    }

    private static BlockPayload ReadPayload(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return new EmptyPayload();
        var kind = el.TryGetProperty("kind", out var k) ? k.GetString() : null;
        return kind switch
        {
            "equation" => new EquationPayload(el.TryGetProperty("latex", out var lx) ? lx.GetString() ?? string.Empty : string.Empty),
            "image" => new ImagePayload(
                el.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty,
                el.TryGetProperty("alt", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                el.TryGetProperty("width", out var w) && w.TryGetDouble(out var wd) ? wd : 0,
                el.TryGetProperty("align", out var al) ? al.GetString() ?? "left" : "left"),
            "code" => new CodePayload(
                el.TryGetProperty("language", out var lang) ? lang.GetString() ?? "csharp" : "csharp",
                el.TryGetProperty("source", out var src) ? src.GetString() ?? string.Empty : string.Empty),
            "checklist" => new ChecklistPayload(
                el.TryGetProperty("checked", out var ch) && ch.ValueKind == JsonValueKind.True),
            "twoColumn" => new TwoColumnPayload(
                el.TryGetProperty("splitRatio", out var sr) && sr.TryGetDouble(out var s) ? s : 0.5),
            "empty" => new EmptyPayload(),
            null or "" => new EmptyPayload(),
            _ => throw new JsonException($"Unknown block payload kind '{kind}'.")
        };
    }

    private static List<InlineSpan> ReadSpans(JsonElement arr)
    {
        var list = new List<InlineSpan>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            var kind = el.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var isEquation = kind == "equation"
                || (kind != "text" && el.TryGetProperty("latex", out _) && !el.TryGetProperty("text", out _));
            if (isEquation)
            {
                var latex = el.TryGetProperty("latex", out var lx) ? lx.GetString() ?? string.Empty : string.Empty;
                list.Add(new EquationSpan(latex, ReadTextStyle(el)));
            }
            else
            {
                var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                list.Add(new TextSpan(text, ReadTextStyle(el)));
            }
        }

        return list.Count == 0 ? new List<InlineSpan> { InlineSpan.Plain(string.Empty) } : list;
    }

    private static TextStyle ReadTextStyle(JsonElement spanEl)
    {
        if (!spanEl.TryGetProperty("style", out var st) || st.ValueKind != JsonValueKind.Object)
            return TextStyle.Default;
        return LegacyStyleFromJson(st, out _);
    }

    private static void WriteSpans(Utf8JsonWriter writer, IReadOnlyList<InlineSpan> spans)
    {
        writer.WriteStartArray();
        foreach (var s in spans)
        {
            writer.WriteStartObject();
            switch (s)
            {
                case TextSpan t:
                    writer.WriteString("kind", "text");
                    writer.WriteString("text", t.Text);
                    writer.WritePropertyName("style");
                    WriteTextStyle(writer, t.Style);
                    break;
                case EquationSpan e:
                    writer.WriteString("kind", "equation");
                    writer.WriteString("latex", e.Latex);
                    writer.WritePropertyName("style");
                    WriteTextStyle(writer, e.Style);
                    break;
                default:
                    throw new UnreachableException($"Unknown inline span type: {s.GetType().Name}");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTextStyle(Utf8JsonWriter writer, TextStyle st)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("bold", st.Bold);
        writer.WriteBoolean("italic", st.Italic);
        writer.WriteBoolean("underline", st.Underline);
        writer.WriteBoolean("strikethrough", st.Strikethrough);
        writer.WriteBoolean("code", st.Code);
        if (st.BackgroundColor != null)
            writer.WriteString("backgroundColor", st.BackgroundColor);
        if (st.LinkUrl != null)
            writer.WriteString("linkUrl", st.LinkUrl);
        writer.WriteBoolean("suppressAutoLink", st.SuppressAutoLink);
        writer.WriteEndObject();
    }

    private static void WritePayload(Utf8JsonWriter writer, BlockPayload payload)
    {
        writer.WriteStartObject();
        switch (payload)
        {
            case EmptyPayload:
                writer.WriteString("kind", "empty");
                break;
            case EquationPayload eq:
                writer.WriteString("kind", "equation");
                writer.WriteString("latex", eq.Latex);
                break;
            case ImagePayload img:
                writer.WriteString("kind", "image");
                writer.WriteString("path", img.Path);
                writer.WriteString("alt", img.Alt);
                writer.WriteNumber("width", img.Width);
                writer.WriteString("align", img.Align);
                break;
            case CodePayload code:
                writer.WriteString("kind", "code");
                writer.WriteString("language", code.Language);
                writer.WriteString("source", code.Source);
                break;
            case ChecklistPayload cl:
                writer.WriteString("kind", "checklist");
                writer.WriteBoolean("checked", cl.Checked);
                break;
            case TwoColumnPayload tc:
                writer.WriteString("kind", "twoColumn");
                writer.WriteNumber("splitRatio", tc.SplitRatio);
                break;
            default:
                throw new UnreachableException($"Unknown block payload type: {payload.GetType().Name}");
        }

        writer.WriteEndObject();
    }

    private static BlockPayload PayloadFromLegacyMeta(BlockType type, Dictionary<string, object> meta, string? legacyContent)
    {
        switch (type)
        {
            case BlockType.Equation:
                return new EquationPayload(ReadMetaString(meta, "equationLatex"));
            case BlockType.Code:
                return new CodePayload(
                    ReadMetaString(meta, "language"),
                    legacyContent ?? string.Empty);
            case BlockType.Image:
                return new ImagePayload(
                    ReadMetaString(meta, "imagePath"),
                    ReadMetaString(meta, "imageAlt"),
                    ReadMetaDouble(meta, "imageWidth"),
                    ReadMetaString(meta, "imageAlign"));
            case BlockType.Checklist:
                return new ChecklistPayload(ReadMetaBool(meta, "checked"));
            case BlockType.TwoColumn:
                return new TwoColumnPayload(NormalizeSplitRatio(ReadMetaDouble(meta, "columnSplitRatio")));
            default:
                return new EmptyPayload();
        }
    }

    private static string ReadMetaString(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v == null) return string.Empty;
        if (v is string s) return s;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return v.ToString() ?? string.Empty;
    }

    private static double ReadMetaDouble(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v == null) return 0;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is JsonElement je && je.TryGetDouble(out var x)) return x;
        return 0;
    }

    private static bool ReadMetaBool(Dictionary<string, object> meta, string key)
    {
        if (!meta.TryGetValue(key, out var v) || v == null) return false;
        if (v is bool b) return b;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.True;
        return false;
    }

    private static double NormalizeSplitRatio(double raw)
    {
        if (raw <= 0 || raw >= 1 || double.IsNaN(raw))
            return 0.5;
        return Math.Clamp(raw, 0.1, 0.9);
    }

    private static List<InlineSpan> LegacyRunsToSpans(JsonElement runsEl)
    {
        var list = new List<InlineSpan>();
        foreach (var el in runsEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            TextStyle style = default;
            string? eq = null;
            if (el.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.Object)
            {
                style = LegacyStyleFromJson(st, out eq);
            }

            if (!string.IsNullOrEmpty(eq))
                list.Add(new EquationSpan(EquationLatexNormalizer.Normalize(eq), style));
            else
                list.Add(new TextSpan(text, style));
        }

        return list.Count == 0 ? new List<InlineSpan> { InlineSpan.Plain(string.Empty) } : list;
    }

    private static TextStyle LegacyStyleFromJson(JsonElement st, out string? equationLatex)
    {
        equationLatex = null;
        if (st.TryGetProperty("equationLatex", out var eqEl))
        {
            if (eqEl.ValueKind == JsonValueKind.String)
                equationLatex = eqEl.GetString();
        }

        bool B(string n)
        {
            if (!st.TryGetProperty(n, out var x)) return false;
            return x.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => false
            };
        }
        string? S(string n) =>
            st.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;

        return new TextStyle(
            Bold: B("bold"),
            Italic: B("italic"),
            Underline: B("underline"),
            Strikethrough: B("strikethrough"),
            Code: B("code"),
            BackgroundColor: S("backgroundColor"),
            LinkUrl: S("linkUrl"),
            SuppressAutoLink: B("suppressAutoLink"));
    }

    public override void Write(Utf8JsonWriter writer, Block value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("type", value.Type.ToString());
        writer.WriteNumber("order", value.Order);

        writer.WritePropertyName("spans");
        WriteSpans(writer, value.Spans);

        writer.WritePropertyName("payload");
        var payloadToWrite = value.Payload;
        if (value.Type == BlockType.TwoColumn && payloadToWrite is EmptyPayload)
            payloadToWrite = new TwoColumnPayload(NormalizeSplitRatio(ReadMetaDouble(value.Meta ?? new Dictionary<string, object>(), "columnSplitRatio")));
        WritePayload(writer, payloadToWrite);

        writer.WritePropertyName("meta");
        if (value.Type == BlockType.TwoColumn)
        {
            var m = new Dictionary<string, object>(value.Meta ?? new Dictionary<string, object>());
            m.Remove("columnSplitRatio");
            JsonSerializer.Serialize(writer, m, options);
        }
        else if (value.Type == BlockType.Image)
        {
            var m = new Dictionary<string, object>(value.Meta ?? new Dictionary<string, object>());
            foreach (var k in new[] { "imagePath", "imageAlt", "imageWidth", "imageAlign" })
                m.Remove(k);
            JsonSerializer.Serialize(writer, m, options);
        }
        else
            JsonSerializer.Serialize(writer, value.Meta, options);

        if (value.Children is { Count: > 0 })
        {
            writer.WritePropertyName("children");
            JsonSerializer.Serialize(writer, value.Children, options);
        }

        writer.WriteEndObject();
    }
}
