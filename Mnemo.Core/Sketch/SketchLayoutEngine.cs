using System;
using System.Collections.Generic;
using System.Linq;

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
        var laidOutNodes = new List<LaidOutSketchNode>();

        foreach (var node in diagram.Nodes)
        {
            var rank = ranks.TryGetValue(node.Id, out var knownRank) ? knownRank : 0;
            rankOffsets.TryGetValue(rank, out var offsetInRank);
            var labelLines = SketchTextWrapping.WrapLabel(node.Label);
            var labelWidth = labelLines.Max(SketchTextWrapping.MeasureLineWidth);
            var nodeWidth = Math.Clamp(labelWidth + NodeHorizontalPadding, MinNodeWidth, MaxNodeWidth);
            var nodeHeight = Math.Max(
                48,
                labelLines.Count * SketchTextWrapping.LineHeight + NodeVerticalPadding);
            var nodeY = Padding + offsetInRank;
            rankOffsets[rank] = offsetInRank + nodeHeight + VerticalGap;

            laidOutNodes.Add(new LaidOutSketchNode(
                node.Id,
                node.Label,
                labelLines,
                Padding + rank * (MaxNodeWidth + HorizontalGap),
                nodeY,
                nodeWidth,
                nodeHeight));
        }

        var byId = laidOutNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var laidOutEdges = diagram.Edges
            .Where(e => byId.ContainsKey(e.SourceId) && byId.ContainsKey(e.TargetId))
            .Select(e =>
            {
                var source = byId[e.SourceId];
                var target = byId[e.TargetId];
                return new LaidOutSketchEdge(
                    e.Id,
                    e.SourceId,
                    e.TargetId,
                    e.Label,
                    source.X + source.Width,
                    source.Y + source.Height / 2,
                    target.X,
                    target.Y + target.Height / 2);
            })
            .ToArray();

        var width = laidOutNodes.Count == 0 ? 2 * Padding : laidOutNodes.Max(n => n.X + n.Width + Padding);
        var height = laidOutNodes.Count == 0 ? 2 * Padding : laidOutNodes.Max(n => n.Y + n.Height + Padding);
        return new LaidOutSketchDiagram(laidOutNodes, laidOutEdges, new SketchBounds(width, height), diagram.Diagnostics);
    }

    private static Dictionary<string, int> ComputeRanks(ResolvedSketchDiagram diagram)
    {
        var ranks = diagram.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(ranks.Keys, StringComparer.Ordinal);

        for (var i = 0; i < diagram.Nodes.Count; i++)
        {
            var changed = false;
            foreach (var edge in diagram.Edges)
            {
                if (!nodeIds.Contains(edge.SourceId) || !nodeIds.Contains(edge.TargetId))
                    continue;

                var targetRank = Math.Max(ranks[edge.TargetId], ranks[edge.SourceId] + 1);
                if (targetRank == ranks[edge.TargetId])
                    continue;

                ranks[edge.TargetId] = targetRank;
                changed = true;
            }

            if (!changed)
                break;
        }

        return ranks;
    }
}
