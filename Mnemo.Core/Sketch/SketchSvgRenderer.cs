using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Mnemo.Core.Sketch;

public sealed class SketchSvgRenderer
{
    // SVG numbers must use '.' as the decimal separator regardless of host culture
    // (otherwise polygon "points" attributes, coordinates, and stroke widths become invalid).
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public SketchSvgRenderResult Render(LaidOutSketchDiagram diagram)
    {
        var sb = new StringBuilder(256 + diagram.Nodes.Count * 192 + diagram.Edges.Count * 160 + diagram.Groups.Count * 96);
        sb.Append(Inv, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{diagram.Bounds.Width:0.##}" height="{diagram.Bounds.Height:0.##}" viewBox="0 0 {diagram.Bounds.Width:0.##} {diagram.Bounds.Height:0.##}" role="img">""");
        sb.AppendLine();
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"transparent\" />");

        // Groups are rendered first so they appear behind edges and nodes
        foreach (var group in diagram.Groups)
            RenderGroup(sb, group);

        var labelPlacements = BuildLabelPlacements(diagram.Edges);

        foreach (var edge in diagram.Edges)
        {
            var edgeStroke = ResolveColor(edge.Style.Stroke, "#64748b");
            var edgeStrokeWidth = edge.Style.StrokeWidth ?? 2;
            var directionAttr = EdgeDirectionAttribute(edge.Direction);
            var dashAttr = EdgeDashAttribute(edge.Style.LineStyle, edgeStrokeWidth);
            sb.Append(Inv, $"""<line id="{EscapeAttribute(edge.Id)}" x1="{edge.X1:0.##}" y1="{edge.Y1:0.##}" x2="{edge.X2:0.##}" y2="{edge.Y2:0.##}" stroke="{edgeStroke}" stroke-width="{edgeStrokeWidth:0.##}"{dashAttr}{directionAttr} />""");
            sb.AppendLine();

            if (edge.Direction != SketchEdgeDirection.Undirected)
            {
                var forwardArrow = FormatArrowHeadPoints(edge.X1, edge.Y1, edge.X2, edge.Y2);
                if (forwardArrow.Length > 0)
                {
                    sb.Append(Inv, $"""<polygon points="{forwardArrow}" fill="{edgeStroke}" />""");
                    sb.AppendLine();
                }
            }

            if (edge.Direction == SketchEdgeDirection.Bidirectional)
            {
                var reverseArrow = FormatArrowHeadPoints(edge.X2, edge.Y2, edge.X1, edge.Y1);
                if (reverseArrow.Length > 0)
                {
                    sb.Append(Inv, $"""<polygon points="{reverseArrow}" fill="{edgeStroke}" />""");
                    sb.AppendLine();
                }
            }

            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var labelPlacement = labelPlacements[edge.Id];
                sb.Append(Inv, $"""<rect x="{labelPlacement.X:0.##}" y="{labelPlacement.Y:0.##}" width="{labelPlacement.Width:0.##}" height="{labelPlacement.Height:0.##}" rx="4" fill="#ffffff" fill-opacity="0.88" />""");
                sb.AppendLine();
                sb.Append(Inv, $"""<text x="{labelPlacement.TextX:0.##}" y="{labelPlacement.TextY:0.##}" text-anchor="middle" font-family="Inter,Segoe UI,sans-serif" font-size="11" fill="{edgeStroke}">{EscapeText(edge.Label!)}</text>""");
                sb.AppendLine();
            }
        }

        foreach (var node in diagram.Nodes)
        {
            var fill = ResolveColor(node.Style.Fill, "#f8fafc");
            var stroke = ResolveColor(node.Style.Stroke, "#94a3b8");
            var strokeWidth = node.Style.StrokeWidth ?? 1.5;
            var shape = (node.Style.Shape ?? "rounded-rect").ToLowerInvariant();
            var cx = node.X + node.Width / 2;
            var cy = node.Y + node.Height / 2;

            sb.AppendLine($"""<g id="{EscapeAttribute("node:" + node.Id)}" role="group" aria-label="{EscapeAttribute(node.Label)}">""");

            if (!string.IsNullOrWhiteSpace(node.Style.Tooltip))
            {
                sb.AppendLine($"<title>{EscapeText(node.Style.Tooltip)}</title>");
            }

            switch (shape)
            {
                case "circle":
                {
                    var r = Math.Min(node.Width, node.Height) / 2;
                    sb.Append(Inv, $"""<circle cx="{cx:0.##}" cy="{cy:0.##}" r="{r:0.##}" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" />""");
                    sb.AppendLine();
                    break;
                }
                case "diamond":
                    sb.Append(Inv, $"""<polygon points="{cx:0.##},{node.Y:0.##} {node.X + node.Width:0.##},{cy:0.##} {cx:0.##},{node.Y + node.Height:0.##} {node.X:0.##},{cy:0.##}" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" />""");
                    sb.AppendLine();
                    break;
                case "rect":
                    sb.Append(Inv, $"""<rect x="{node.X:0.##}" y="{node.Y:0.##}" width="{node.Width:0.##}" height="{node.Height:0.##}" rx="2" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" />""");
                    sb.AppendLine();
                    break;
                default: // rounded-rect and anything unknown
                    sb.Append(Inv, $"""<rect x="{node.X:0.##}" y="{node.Y:0.##}" width="{node.Width:0.##}" height="{node.Height:0.##}" rx="10" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" />""");
                    sb.AppendLine();
                    break;
            }

            var firstLineY = cy
                - ((node.LabelLines.Count - 1) * SketchTextWrapping.LineHeight / 2)
                + 5;
            for (var i = 0; i < node.LabelLines.Count; i++)
            {
                var lineY = firstLineY + i * SketchTextWrapping.LineHeight;
                sb.Append(Inv, $"""<text x="{cx:0.##}" y="{lineY:0.##}" text-anchor="middle" font-family="Inter,Segoe UI,sans-serif" font-size="14" fill="#0f172a">{EscapeText(node.LabelLines[i])}</text>""");
                sb.AppendLine();
            }
            sb.AppendLine("</g>");
        }

        sb.Append("</svg>");
        return new SketchSvgRenderResult(sb.ToString(), diagram.Bounds, diagram.Diagnostics);
    }

    private static void RenderGroup(StringBuilder sb, LaidOutSketchGroup group)
    {
        var fill = ResolveColor(group.Style.Fill, "#f1f5f9");
        var stroke = ResolveColor(group.Style.Stroke, "#cbd5e1");
        var strokeWidth = group.Style.StrokeWidth ?? 1;

        sb.AppendLine($"""<g id="{EscapeAttribute("group:" + group.Id)}">""");
        sb.Append(Inv, $"""<rect x="{group.X:0.##}" y="{group.Y:0.##}" width="{group.Width:0.##}" height="{group.Height:0.##}" rx="8" fill="{fill}" fill-opacity="0.6" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" stroke-dasharray="4 3" />""");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(group.Label))
        {
            sb.Append(Inv, $"""<text x="{group.X + 10:0.##}" y="{group.Y + 14:0.##}" font-family="Inter,Segoe UI,sans-serif" font-size="11" font-weight="600" fill="{stroke}">{EscapeText(group.Label)}</text>""");
            sb.AppendLine();
        }
        sb.AppendLine("</g>");
    }

    private static string EdgeDashAttribute(SketchEdgeLineStyle? lineStyle, double strokeWidth) =>
        lineStyle switch
        {
            SketchEdgeLineStyle.Dashed => string.Create(Inv, $" stroke-dasharray=\"{strokeWidth * 4:0.##} {strokeWidth * 2:0.##}\""),
            SketchEdgeLineStyle.Dotted => string.Create(Inv, $" stroke-dasharray=\"{strokeWidth:0.##} {strokeWidth * 2:0.##}\""),
            _ => string.Empty
        };

    private static Dictionary<string, LabelPlacement> BuildLabelPlacements(IReadOnlyList<LaidOutSketchEdge> edges)
    {
        // Build a fast lookup so later steps don't scan the list.
        var labeled = new Dictionary<string, LaidOutSketchEdge>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!string.IsNullOrWhiteSpace(edge.Label))
                labeled[edge.Id] = edge;
        }

        if (labeled.Count == 0)
            return new Dictionary<string, LabelPlacement>(StringComparer.Ordinal);

        // Assign a symmetric Y offset to each labeled edge by grouping edges that share
        // a node (fan-out from the same source, or fan-in to the same target).  Within
        // each group, edges are sorted left-to-right by their midpoint X so that the
        // assignment is stable regardless of parse order; the resulting offsets are
        // centered around zero: e.g. for a 2-edge fan, −SlotStep/2 and +SlotStep/2.
        // An edge that belongs to two groups (fan-out and fan-in simultaneously) takes
        // the offset with the larger magnitude.
        const double slotStep = 18;
        var yOffsets = new Dictionary<string, double>(labeled.Count, StringComparer.Ordinal);
        foreach (var id in labeled.Keys)
            yOffsets[id] = 0;

        AssignGroupOffsets(labeled, yOffsets, slotStep, edge => edge.SourceId);
        AssignGroupOffsets(labeled, yOffsets, slotStep, edge => edge.TargetId);

        var placements = new Dictionary<string, LabelPlacement>(labeled.Count, StringComparer.Ordinal);
        foreach (var (id, edge) in labeled)
            placements[id] = CreateLabelPlacement(edge, yOffsets[id]);

        return placements;
    }

    private static void AssignGroupOffsets(
        IReadOnlyDictionary<string, LaidOutSketchEdge> labeled,
        Dictionary<string, double> yOffsets,
        double slotStep,
        Func<LaidOutSketchEdge, string> groupKeySelector)
    {
        // Collect edges into groups keyed by the shared node id.
        var groups = new Dictionary<string, List<LaidOutSketchEdge>>(StringComparer.Ordinal);
        foreach (var edge in labeled.Values)
        {
            var key = groupKeySelector(edge);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add(edge);
        }

        foreach (var group in groups.Values)
        {
            if (group.Count < 2)
                continue;

            // Sort by midpoint X for a stable, visually natural left-to-right order.
            group.Sort((a, b) => ((a.X1 + a.X2) / 2).CompareTo((b.X1 + b.X2) / 2));

            var n = group.Count;
            for (var i = 0; i < n; i++)
            {
                // Symmetric distribution centred on 0: e.g. n=2 → -9, +9; n=3 → -18, 0, +18.
                var offset = (i - (n - 1) / 2.0) * slotStep;
                var existing = yOffsets[group[i].Id];
                if (Math.Abs(offset) > Math.Abs(existing))
                    yOffsets[group[i].Id] = offset;
            }
        }
    }

    private static LabelPlacement CreateLabelPlacement(LaidOutSketchEdge edge, double verticalOffset)
    {
        const double labelHeight = 15;
        var textX = (edge.X1 + edge.X2) / 2;
        var textY = (edge.Y1 + edge.Y2) / 2 - 2 + verticalOffset;
        // Approximate label width for a readable pill-shaped background.
        var width = edge.Label!.Length * 7.6 + 12;
        return new LabelPlacement(textX - width / 2, textY - 12, width, labelHeight, textX, textY);
    }

    private static string EdgeDirectionAttribute(SketchEdgeDirection direction) =>
        direction switch
        {
            SketchEdgeDirection.Undirected => " sketch-edge-direction=\"undirected\"",
            SketchEdgeDirection.Bidirectional => " sketch-edge-direction=\"bidirectional\"",
            _ => string.Empty
        };

    private static string EscapeText(string value) => WebUtility.HtmlEncode(value);

    private static string EscapeAttribute(string value) => WebUtility.HtmlEncode(value);

    private static string FormatArrowHeadPoints(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.1)
            return string.Empty;

        var ux = dx / length;
        var uy = dy / length;
        const double size = 9;
        const double halfWidth = 4.5;
        var baseX = x2 - ux * size;
        var baseY = y2 - uy * size;
        var normalX = -uy;
        var normalY = ux;
        return string.Create(Inv, $"{x2:0.##},{y2:0.##} {baseX + normalX * halfWidth:0.##},{baseY + normalY * halfWidth:0.##} {baseX - normalX * halfWidth:0.##},{baseY - normalY * halfWidth:0.##}");
    }

    private static string ResolveColor(SketchColorValue? value, string fallback)
    {
        if (value == null)
            return fallback;

        return value.Kind switch
        {
            SketchColorKind.Hex => NormalizeHex(value.Value, fallback),
            SketchColorKind.Rgb => $"rgb({value.Value})",
            SketchColorKind.Rgba => $"rgba({value.Value})",
            SketchColorKind.Theme => $"theme({value.Value})",
            SketchColorKind.Named => ResolveNamedColor(value.Value, fallback),
            _ => fallback
        };
    }

    // FrozenDictionary: built once, lookup is faster than Dictionary; OrdinalIgnoreCase avoids per-call ToLowerInvariant alloc.
    private static readonly FrozenDictionary<string, string> NamedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = "transparent",
        ["white"] = "#ffffff",
        ["black"] = "#000000",
        ["gray"] = "#6b7280",
        ["grey"] = "#6b7280",
        ["slate"] = "#64748b",
        ["red"] = "#ef4444",
        ["orange"] = "#f97316",
        ["amber"] = "#f59e0b",
        ["yellow"] = "#eab308",
        ["lime"] = "#84cc16",
        ["green"] = "#22c55e",
        ["emerald"] = "#10b981",
        ["teal"] = "#14b8a6",
        ["cyan"] = "#06b6d4",
        ["sky"] = "#0ea5e9",
        ["blue"] = "#3b82f6",
        ["indigo"] = "#6366f1",
        ["violet"] = "#8b5cf6",
        ["purple"] = "#a855f7",
        ["fuchsia"] = "#d946ef",
        ["pink"] = "#ec4899",
        ["rose"] = "#f43f5e",
        // Slate scale
        ["slate-50"] = "#f8fafc",
        ["slate-100"] = "#f1f5f9",
        ["slate-200"] = "#e2e8f0",
        ["slate-300"] = "#cbd5e1",
        ["slate-400"] = "#94a3b8",
        ["slate-500"] = "#64748b",
        ["slate-600"] = "#475569",
        ["slate-700"] = "#334155",
        ["slate-800"] = "#1e293b",
        ["slate-900"] = "#0f172a",
        // Gray scale
        ["gray-50"] = "#f9fafb",
        ["gray-100"] = "#f3f4f6",
        ["gray-200"] = "#e5e7eb",
        ["gray-300"] = "#d1d5db",
        ["gray-400"] = "#9ca3af",
        ["gray-500"] = "#6b7280",
        ["gray-600"] = "#4b5563",
        ["gray-700"] = "#374151",
        ["gray-800"] = "#1f2937",
        ["gray-900"] = "#111827",
        // Blue scale
        ["blue-50"] = "#eff6ff",
        ["blue-100"] = "#dbeafe",
        ["blue-200"] = "#bfdbfe",
        ["blue-300"] = "#93c5fd",
        ["blue-400"] = "#60a5fa",
        ["blue-500"] = "#3b82f6",
        ["blue-600"] = "#2563eb",
        ["blue-700"] = "#1d4ed8",
        ["blue-800"] = "#1e40af",
        ["blue-900"] = "#1e3a8a",
        // Green scale
        ["green-50"] = "#f0fdf4",
        ["green-100"] = "#dcfce7",
        ["green-200"] = "#bbf7d0",
        ["green-300"] = "#86efac",
        ["green-400"] = "#4ade80",
        ["green-500"] = "#22c55e",
        ["green-600"] = "#16a34a",
        ["green-700"] = "#15803d",
        ["green-800"] = "#166534",
        ["green-900"] = "#14532d",
        // Red scale
        ["red-50"] = "#fef2f2",
        ["red-100"] = "#fee2e2",
        ["red-200"] = "#fecaca",
        ["red-300"] = "#fca5a5",
        ["red-400"] = "#f87171",
        ["red-500"] = "#ef4444",
        ["red-600"] = "#dc2626",
        ["red-700"] = "#b91c1c",
        ["red-800"] = "#991b1b",
        ["red-900"] = "#7f1d1d",
        // Yellow scale
        ["yellow-50"] = "#fefce8",
        ["yellow-100"] = "#fef9c3",
        ["yellow-200"] = "#fef08a",
        ["yellow-300"] = "#fde047",
        ["yellow-400"] = "#facc15",
        ["yellow-500"] = "#eab308",
        ["yellow-600"] = "#ca8a04",
        ["yellow-700"] = "#a16207",
        ["yellow-800"] = "#854d0e",
        ["yellow-900"] = "#713f12",
        // Purple scale
        ["purple-50"] = "#faf5ff",
        ["purple-100"] = "#f3e8ff",
        ["purple-200"] = "#e9d5ff",
        ["purple-300"] = "#d8b4fe",
        ["purple-400"] = "#c084fc",
        ["purple-500"] = "#a855f7",
        ["purple-600"] = "#9333ea",
        ["purple-700"] = "#7e22ce",
        ["purple-800"] = "#6b21a8",
        ["purple-900"] = "#581c87",
        // Pink scale
        ["pink-50"] = "#fdf2f8",
        ["pink-100"] = "#fce7f3",
        ["pink-200"] = "#fbcfe8",
        ["pink-300"] = "#f9a8d4",
        ["pink-400"] = "#f472b6",
        ["pink-500"] = "#ec4899",
        ["pink-600"] = "#db2777",
        ["pink-700"] = "#be185d",
        ["pink-800"] = "#9d174d",
        ["pink-900"] = "#831843",
        // Indigo scale
        ["indigo-50"] = "#eef2ff",
        ["indigo-100"] = "#e0e7ff",
        ["indigo-200"] = "#c7d2fe",
        ["indigo-300"] = "#a5b4fc",
        ["indigo-400"] = "#818cf8",
        ["indigo-500"] = "#6366f1",
        ["indigo-600"] = "#4f46e5",
        ["indigo-700"] = "#4338ca",
        ["indigo-800"] = "#3730a3",
        ["indigo-900"] = "#312e81",
        // Teal scale
        ["teal-50"] = "#f0fdfa",
        ["teal-100"] = "#ccfbf1",
        ["teal-200"] = "#99f6e4",
        ["teal-300"] = "#5eead4",
        ["teal-400"] = "#2dd4bf",
        ["teal-500"] = "#14b8a6",
        ["teal-600"] = "#0d9488",
        ["teal-700"] = "#0f766e",
        ["teal-800"] = "#115e59",
        ["teal-900"] = "#134e4a",
        // Orange scale
        ["orange-50"] = "#fff7ed",
        ["orange-100"] = "#ffedd5",
        ["orange-200"] = "#fed7aa",
        ["orange-300"] = "#fdba74",
        ["orange-400"] = "#fb923c",
        ["orange-500"] = "#f97316",
        ["orange-600"] = "#ea580c",
        ["orange-700"] = "#c2410c",
        ["orange-800"] = "#9a3412",
        ["orange-900"] = "#7c2d12",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string ResolveNamedColor(string value, string fallback)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return fallback;

        var lookup = NamedColors.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.TryGetValue(trimmed, out var hex) ? hex : fallback;
    }

    private static string NormalizeHex(string value, string fallback)
    {
        var span = value.AsSpan().Trim();
        var hasHash = span.Length > 0 && span[0] == '#';
        var digits = hasHash ? span[1..] : span;
        if (digits.Length is not (3 or 4 or 6 or 8))
            return fallback;

        for (var i = 0; i < digits.Length; i++)
        {
            if (!Uri.IsHexDigit(digits[i]))
                return fallback;
        }

        if (hasHash && span.Length == value.Length)
            return value;

        return hasHash ? span.ToString() : string.Concat("#", digits);
    }

    private readonly record struct LabelPlacement(double X, double Y, double Width, double Height, double TextX, double TextY)
    {
        public bool Intersects(LabelPlacement other) =>
            X < other.X + other.Width
            && X + Width > other.X
            && Y < other.Y + other.Height
            && Y + Height > other.Y;
    }
}
