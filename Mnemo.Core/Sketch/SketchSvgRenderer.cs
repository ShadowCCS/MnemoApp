using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Mnemo.Core.Sketch;

public sealed class SketchSvgRenderer
{
    public SketchSvgRenderResult Render(LaidOutSketchDiagram diagram)
    {
        var sb = new StringBuilder(256 + diagram.Nodes.Count * 192 + diagram.Edges.Count * 160);
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{diagram.Bounds.Width:0.##}" height="{diagram.Bounds.Height:0.##}" viewBox="0 0 {diagram.Bounds.Width:0.##} {diagram.Bounds.Height:0.##}" role="img">""");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"transparent\" />");

        foreach (var edge in diagram.Edges)
        {
            var edgeStroke = ResolveColor(edge.Style.Stroke, "#64748b");
            var edgeStrokeWidth = edge.Style.StrokeWidth ?? 2;
            sb.AppendLine($"""<line id="{EscapeAttribute(edge.Id)}" x1="{edge.X1:0.##}" y1="{edge.Y1:0.##}" x2="{edge.X2:0.##}" y2="{edge.Y2:0.##}" stroke="{edgeStroke}" stroke-width="{edgeStrokeWidth:0.##}" />""");
            var arrowPoints = FormatArrowHeadPoints(edge.X1, edge.Y1, edge.X2, edge.Y2);
            if (arrowPoints.Length > 0)
                sb.AppendLine($"""<polygon points="{arrowPoints}" fill="{edgeStroke}" />""");
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var labelX = (edge.X1 + edge.X2) / 2;
                var labelY = (edge.Y1 + edge.Y2) / 2 - 2;
                sb.AppendLine($"""<text x="{labelX:0.##}" y="{labelY:0.##}" text-anchor="middle" font-family="Inter,Segoe UI,sans-serif" font-size="12" fill="{edgeStroke}">{EscapeText(edge.Label!)}</text>""");
            }
        }

        foreach (var node in diagram.Nodes)
        {
            var fill = ResolveColor(node.Style.Fill, "#f8fafc");
            var stroke = ResolveColor(node.Style.Stroke, "#94a3b8");
            var strokeWidth = node.Style.StrokeWidth ?? 1.5;
            var radius = string.Equals(node.Style.Shape, "rect", StringComparison.OrdinalIgnoreCase)
                ? 2
                : 10;

            sb.AppendLine($"""<g id="{EscapeAttribute("node:" + node.Id)}">""");
            sb.AppendLine($"""<rect x="{node.X:0.##}" y="{node.Y:0.##}" width="{node.Width:0.##}" height="{node.Height:0.##}" rx="{radius}" fill="{fill}" stroke="{stroke}" stroke-width="{strokeWidth:0.##}" />""");
            var firstLineY = node.Y
                + node.Height / 2
                - ((node.LabelLines.Count - 1) * SketchTextWrapping.LineHeight / 2)
                + 5;
            for (var i = 0; i < node.LabelLines.Count; i++)
            {
                var lineY = firstLineY + i * SketchTextWrapping.LineHeight;
                sb.AppendLine($"""<text x="{node.X + node.Width / 2:0.##}" y="{lineY:0.##}" text-anchor="middle" font-family="Inter,Segoe UI,sans-serif" font-size="14" fill="#0f172a">{EscapeText(node.LabelLines[i])}</text>""");
            }
            sb.AppendLine("</g>");
        }

        sb.Append("</svg>");
        return new SketchSvgRenderResult(sb.ToString(), diagram.Bounds, diagram.Diagnostics);
    }

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
        return $"{x2:0.##},{y2:0.##} {baseX + normalX * halfWidth:0.##},{baseY + normalY * halfWidth:0.##} {baseX - normalX * halfWidth:0.##},{baseY - normalY * halfWidth:0.##}";
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
        ["slate-50"] = "#f8fafc",
        ["slate-100"] = "#f1f5f9",
        ["slate-500"] = "#64748b",
        ["slate-700"] = "#334155",
        ["blue-50"] = "#eff6ff",
        ["blue-100"] = "#dbeafe",
        ["blue-500"] = "#3b82f6",
        ["blue-700"] = "#1d4ed8",
        ["green-100"] = "#dcfce7",
        ["green-700"] = "#15803d",
        ["red-100"] = "#fee2e2",
        ["red-700"] = "#b91c1c",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string ResolveNamedColor(string value, string fallback)
    {
        // Trim only when needed (avoids alloc on the common path).
        var trimmed = value.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return fallback;

        // Dictionary alternate lookup avoids allocating a string from the span when key is already interned.
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

        // Avoid alloc when input is already canonical "#xxx".
        if (hasHash && span.Length == value.Length)
            return value;

        return hasHash ? span.ToString() : string.Concat("#", digits);
    }
}
