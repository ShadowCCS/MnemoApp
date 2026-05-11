using System;
using System.Linq;
using System.Net;
using System.Text;

namespace Mnemo.Core.Sketch;

public sealed class SketchSvgRenderer
{
    public SketchSvgRenderResult Render(LaidOutSketchDiagram diagram)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{diagram.Bounds.Width:0.##}" height="{diagram.Bounds.Height:0.##}" viewBox="0 0 {diagram.Bounds.Width:0.##} {diagram.Bounds.Height:0.##}" role="img">""");
        sb.AppendLine("<defs><marker id=\"sketch-arrow\" markerWidth=\"10\" markerHeight=\"10\" refX=\"8\" refY=\"3\" orient=\"auto\" markerUnits=\"strokeWidth\"><path d=\"M0,0 L0,6 L9,3 z\" fill=\"#64748b\" /></marker></defs>");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"transparent\" />");

        foreach (var edge in diagram.Edges)
        {
            sb.AppendLine($"""<line id="{EscapeAttribute(edge.Id)}" x1="{edge.X1:0.##}" y1="{edge.Y1:0.##}" x2="{edge.X2:0.##}" y2="{edge.Y2:0.##}" stroke="#64748b" stroke-width="2" marker-end="url(#sketch-arrow)" />""");
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                var labelX = (edge.X1 + edge.X2) / 2;
                var labelY = (edge.Y1 + edge.Y2) / 2 - 2;
                sb.AppendLine($"""<text x="{labelX:0.##}" y="{labelY:0.##}" text-anchor="middle" font-family="Inter,Segoe UI,sans-serif" font-size="12" fill="#475569">{EscapeText(edge.Label)}</text>""");
            }
        }

        foreach (var node in diagram.Nodes)
        {
            sb.AppendLine($"""<g id="{EscapeAttribute("node:" + node.Id)}">""");
            sb.AppendLine($"""<rect x="{node.X:0.##}" y="{node.Y:0.##}" width="{node.Width:0.##}" height="{node.Height:0.##}" rx="10" fill="#f8fafc" stroke="#94a3b8" stroke-width="1.5" />""");
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
}
