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
    private const double HorizontalGap = 80;
    private const double VerticalGap = 56;
    private const double Padding = 32;

    public LaidOutSketchDiagram Layout(ResolvedSketchDiagram diagram)
    {
        var direction = diagram.Meta.Direction;
        var ranks = ComputeRanks(diagram);
        var nodeSizes = ComputeNodeSizes(diagram.Nodes);
        var byRank = GroupByRank(diagram.Nodes, ranks);

        List<LaidOutSketchNode> laidOutNodes = direction is SketchLayoutDirection.TopToBottom or SketchLayoutDirection.BottomToTop
            ? LayoutTopToBottom(diagram.Nodes, byRank, nodeSizes, direction)
            : LayoutLeftToRight(diagram.Nodes, byRank, nodeSizes, direction);

        var byId = new Dictionary<string, LaidOutSketchNode>(laidOutNodes.Count, StringComparer.Ordinal);
        foreach (var n in laidOutNodes)
            byId[n.Id] = n;

        var laidOutEdges = BuildEdges(diagram.Edges, byId, direction);

        double maxRight = 2 * Padding;
        double maxBottom = 2 * Padding;
        foreach (var n in laidOutNodes)
        {
            var r = n.X + n.Width + Padding;
            var b = n.Y + n.Height + Padding;
            if (r > maxRight) maxRight = r;
            if (b > maxBottom) maxBottom = b;
        }

        return new LaidOutSketchDiagram(direction, laidOutNodes, laidOutEdges, new SketchBounds(maxRight, maxBottom), diagram.Diagnostics);
    }

    // ── Sizing ─────────────────────────────────────────────────────────────

    private static Dictionary<string, NodeSize> ComputeNodeSizes(IReadOnlyList<ResolvedSketchNode> nodes)
    {
        var sizes = new Dictionary<string, NodeSize>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var lines = SketchTextWrapping.WrapLabel(node.Label);
            double maxLineWidth = 0;
            foreach (var line in lines)
            {
                var w = SketchTextWrapping.MeasureLineWidth(line);
                if (w > maxLineWidth) maxLineWidth = w;
            }
            var width = Math.Clamp(maxLineWidth + NodeHorizontalPadding, MinNodeWidth, MaxNodeWidth);
            var height = Math.Max(48, lines.Count * SketchTextWrapping.LineHeight + NodeVerticalPadding);
            sizes[node.Id] = new NodeSize(width, height, lines);
        }
        return sizes;
    }

    // ── Rank grouping ───────────────────────────────────────────────────────

    private static Dictionary<int, List<string>> GroupByRank(
        IReadOnlyList<ResolvedSketchNode> nodes,
        IReadOnlyDictionary<string, int> ranks)
    {
        var byRank = new Dictionary<int, List<string>>();
        foreach (var node in nodes)
        {
            var rank = ranks.TryGetValue(node.Id, out var r) ? r : 0;
            if (!byRank.TryGetValue(rank, out var list))
            {
                list = new List<string>();
                byRank[rank] = list;
            }
            list.Add(node.Id);
        }
        return byRank;
    }

    // ── Left-to-right layout ────────────────────────────────────────────────

    private static List<LaidOutSketchNode> LayoutLeftToRight(
        IReadOnlyList<ResolvedSketchNode> nodes,
        Dictionary<int, List<string>> byRank,
        Dictionary<string, NodeSize> nodeSizes,
        SketchLayoutDirection direction)
    {
        // Per-rank metrics: max width for column spacing, total height for vertical centering
        var rankMaxWidth = new Dictionary<int, double>();
        var rankTotalHeight = new Dictionary<int, double>();
        foreach (var (rank, ids) in byRank)
        {
            double maxW = 0;
            double totalH = 0;
            foreach (var id in ids)
            {
                var s = nodeSizes[id];
                if (s.Width > maxW) maxW = s.Width;
                totalH += s.Height;
            }
            totalH += Math.Max(0, ids.Count - 1) * VerticalGap;
            rankMaxWidth[rank] = maxW;
            rankTotalHeight[rank] = totalH;
        }

        // Cumulative X offset per rank
        var rankX = BuildCumulativeOffsets(rankMaxWidth, HorizontalGap);

        // Tallest total height across all ranks (for centering shorter ranks)
        var maxTotalHeight = rankTotalHeight.Values.DefaultIfEmpty(0).Max();

        var result = new Dictionary<string, LaidOutSketchNode>(nodes.Count, StringComparer.Ordinal);
        foreach (var (rank, ids) in byRank)
        {
            var x = Padding + rankX[rank];
            var startY = Padding + (maxTotalHeight - rankTotalHeight[rank]) / 2;

            foreach (var id in ids)
            {
                var s = nodeSizes[id];
                result[id] = new LaidOutSketchNode(id, string.Empty, s.LabelLines, new ResolvedSketchStyle(), x, startY, s.Width, s.Height);
                startY += s.Height + VerticalGap;
            }
        }

        // Reconstruct in original node order, attaching style and label
        return nodes
            .Where(n => result.ContainsKey(n.Id))
            .Select(n =>
            {
                var laid = result[n.Id];
                return new LaidOutSketchNode(n.Id, n.Label, laid.LabelLines, n.Style, laid.X, laid.Y, laid.Width, laid.Height);
            })
            .ToList();
    }

    // ── Top-to-bottom layout ────────────────────────────────────────────────

    private static List<LaidOutSketchNode> LayoutTopToBottom(
        IReadOnlyList<ResolvedSketchNode> nodes,
        Dictionary<int, List<string>> byRank,
        Dictionary<string, NodeSize> nodeSizes,
        SketchLayoutDirection direction)
    {
        // Per-rank metrics: max height for row spacing, total width for horizontal centering
        var rankMaxHeight = new Dictionary<int, double>();
        var rankTotalWidth = new Dictionary<int, double>();
        foreach (var (rank, ids) in byRank)
        {
            double maxH = 0;
            double totalW = 0;
            foreach (var id in ids)
            {
                var s = nodeSizes[id];
                if (s.Height > maxH) maxH = s.Height;
                totalW += s.Width;
            }
            totalW += Math.Max(0, ids.Count - 1) * HorizontalGap;
            rankMaxHeight[rank] = maxH;
            rankTotalWidth[rank] = totalW;
        }

        // Cumulative Y offset per rank
        var rankY = BuildCumulativeOffsets(rankMaxHeight, VerticalGap);

        // Widest total width across all ranks (for centering narrower ranks)
        var maxTotalWidth = rankTotalWidth.Values.DefaultIfEmpty(0).Max();

        var result = new Dictionary<string, LaidOutSketchNode>(nodes.Count, StringComparer.Ordinal);
        foreach (var (rank, ids) in byRank)
        {
            var y = Padding + rankY[rank];
            var startX = Padding + (maxTotalWidth - rankTotalWidth[rank]) / 2;

            foreach (var id in ids)
            {
                var s = nodeSizes[id];
                result[id] = new LaidOutSketchNode(id, string.Empty, s.LabelLines, new ResolvedSketchStyle(), startX, y, s.Width, s.Height);
                startX += s.Width + HorizontalGap;
            }
        }

        return nodes
            .Where(n => result.ContainsKey(n.Id))
            .Select(n =>
            {
                var laid = result[n.Id];
                return new LaidOutSketchNode(n.Id, n.Label, laid.LabelLines, n.Style, laid.X, laid.Y, laid.Width, laid.Height);
            })
            .ToList();
    }

    // ── Edge endpoint calculation ──────────────────────────────────────────

    private static List<LaidOutSketchEdge> BuildEdges(
        IReadOnlyList<ResolvedSketchEdge> edges,
        IReadOnlyDictionary<string, LaidOutSketchNode> byId,
        SketchLayoutDirection direction)
    {
        var result = new List<LaidOutSketchEdge>(edges.Count);
        foreach (var e in edges)
        {
            if (!byId.TryGetValue(e.SourceId, out var source) || !byId.TryGetValue(e.TargetId, out var target))
                continue;

            double x1, y1, x2, y2;
            if (direction is SketchLayoutDirection.TopToBottom or SketchLayoutDirection.BottomToTop)
            {
                x1 = source.X + source.Width / 2;
                y1 = source.Y + source.Height;
                x2 = target.X + target.Width / 2;
                y2 = target.Y;
            }
            else
            {
                x1 = source.X + source.Width;
                y1 = source.Y + source.Height / 2;
                x2 = target.X;
                y2 = target.Y + target.Height / 2;
            }

            result.Add(new LaidOutSketchEdge(e.Id, e.SourceId, e.TargetId, e.Direction, e.Label, e.Style, x1, y1, x2, y2));
        }
        return result;
    }

    // ── Rank computation ───────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a cumulative offset map: rank → sum of sizes of all earlier ranks plus gaps.
    /// </summary>
    private static Dictionary<int, double> BuildCumulativeOffsets(
        Dictionary<int, double> rankSizes,
        double gap)
    {
        if (rankSizes.Count == 0)
            return new Dictionary<int, double>();

        var maxRank = rankSizes.Keys.Max();
        var offsets = new Dictionary<int, double>(rankSizes.Count);
        double cumulative = 0;
        for (var rank = 0; rank <= maxRank; rank++)
        {
            offsets[rank] = cumulative;
            rankSizes.TryGetValue(rank, out var size);
            cumulative += size + gap;
        }
        return offsets;
    }

    private readonly record struct NodeSize(double Width, double Height, IReadOnlyList<string> LabelLines);
}
