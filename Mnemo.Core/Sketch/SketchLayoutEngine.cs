using System;
using System.Collections.Generic;

namespace Mnemo.Core.Sketch;

public sealed class SketchLayoutEngine
{
    private const double MinNodeWidth = 128;
    private const double MaxNodeWidth = 260;
    private const double NodeHorizontalPadding = 32;
    private const double NodeVerticalPadding = 18;
    private const double HorizontalGap = 72;
    private const double VerticalGap = 48;
    private const double Padding = 24;

    public LaidOutSketchDiagram Layout(ResolvedSketchDiagram diagram)
    {
        var ranks = ComputeRanks(diagram);
        var rankOffsets = new Dictionary<int, double>();
        var laidOutNodes = new List<LaidOutSketchNode>(diagram.Nodes.Count);

        foreach (var node in diagram.Nodes)
        {
            var rank = ranks.TryGetValue(node.Id, out var knownRank) ? knownRank : 0;
            rankOffsets.TryGetValue(rank, out var offsetInRank);
            var labelLines = SketchTextWrapping.WrapLabel(node.Label);

            double labelWidth = 0;
            foreach (var line in labelLines)
            {
                var w = SketchTextWrapping.MeasureLineWidth(line);
                if (w > labelWidth) labelWidth = w;
            }

            var nodeWidth = Math.Clamp(labelWidth + NodeHorizontalPadding, MinNodeWidth, MaxNodeWidth);
            var nodeHeight = Math.Max(48, labelLines.Count * SketchTextWrapping.LineHeight + NodeVerticalPadding);
            var nodeY = Padding + offsetInRank;
            rankOffsets[rank] = offsetInRank + nodeHeight + VerticalGap;

            laidOutNodes.Add(new LaidOutSketchNode(
                node.Id,
                node.Label,
                labelLines,
                node.Style,
                Padding + rank * (MaxNodeWidth + HorizontalGap),
                nodeY,
                nodeWidth,
                nodeHeight));
        }

        var byId = new Dictionary<string, LaidOutSketchNode>(laidOutNodes.Count, StringComparer.Ordinal);
        foreach (var n in laidOutNodes)
            byId[n.Id] = n;

        var laidOutEdges = new List<LaidOutSketchEdge>(diagram.Edges.Count);
        foreach (var e in diagram.Edges)
        {
            if (!byId.TryGetValue(e.SourceId, out var source) || !byId.TryGetValue(e.TargetId, out var target))
                continue;

            laidOutEdges.Add(new LaidOutSketchEdge(
                e.Id,
                e.SourceId,
                e.TargetId,
                e.Label,
                e.Style,
                source.X + source.Width,
                source.Y + source.Height / 2,
                target.X,
                target.Y + target.Height / 2));
        }

        double maxRight = 2 * Padding;
        double maxBottom = 2 * Padding;
        if (laidOutNodes.Count > 0)
        {
            maxRight = 0;
            maxBottom = 0;
            foreach (var n in laidOutNodes)
            {
                var r = n.X + n.Width + Padding;
                var b = n.Y + n.Height + Padding;
                if (r > maxRight) maxRight = r;
                if (b > maxBottom) maxBottom = b;
            }
        }

        return new LaidOutSketchDiagram(laidOutNodes, laidOutEdges, new SketchBounds(maxRight, maxBottom), diagram.Diagnostics);
    }

    private static Dictionary<string, int> ComputeRanks(ResolvedSketchDiagram diagram)
    {
        var ranks = new Dictionary<string, int>(diagram.Nodes.Count, StringComparer.Ordinal);
        foreach (var n in diagram.Nodes)
            ranks[n.Id] = 0;

        for (var i = 0; i < diagram.Nodes.Count; i++)
        {
            var changed = false;
            foreach (var edge in diagram.Edges)
            {
                if (!ranks.TryGetValue(edge.SourceId, out var sourceRank)
                    || !ranks.TryGetValue(edge.TargetId, out var targetRank))
                    continue;

                var nextRank = sourceRank + 1;
                if (nextRank <= targetRank)
                    continue;

                ranks[edge.TargetId] = nextRank;
                changed = true;
            }

            if (!changed)
                break;
        }

        return ranks;
    }
}
