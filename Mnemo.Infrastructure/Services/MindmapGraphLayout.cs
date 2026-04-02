using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models.Mindmap;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Computes hierarchical positions for a mindmap using the same spacing as the mindmap editor auto-layout.
/// Operates on the persisted <see cref="Mindmap"/> model only (no UI).
/// </summary>
internal static class MindmapGraphLayout
{
    private const double DefaultWidth = 120;
    private const double DefaultHeight = 40;
    private const double LayoutTreeVerticalHSpacing = 250;
    private const double LayoutTreeVerticalVSpacing = 80;
    private const double LayoutTreeHorizontalVSpacing = 200;
    private const double LayoutTreeHorizontalHSpacing = 120;
    private const double LayoutRadialCenterX = 400;
    private const double LayoutRadialCenterY = 300;
    private const double LayoutRadialRadiusStep = 180;

    public static void Apply(Mindmap mindmap, string algorithm)
    {
        if (mindmap.Nodes.Count == 0)
            return;

        if (string.IsNullOrEmpty(algorithm)
            || string.Equals(algorithm, "Freeform", StringComparison.Ordinal)
            || !IsKnownAlgorithm(algorithm))
            algorithm = LayoutAlgorithms.TreeVertical;

        var metrics = BuildMetrics(mindmap);
        var children = GetHierarchyChildren(mindmap);
        var roots = FindRoots(mindmap);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pos = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        double maxOuter = mindmap.Nodes.Max(n =>
        {
            var (w, h) = metrics(n.Id);
            return Math.Max(w, h);
        });
        double radialRadiusStep = LayoutRadialRadiusStep + Math.Max(0, maxOuter - DefaultWidth);

        switch (algorithm)
        {
            case LayoutAlgorithms.TreeVertical:
                double currentY = 100;
                foreach (var rootId in roots)
                {
                    currentY = LayoutTreeVertical(rootId, 100, currentY, children, visited, pos, metrics);
                    currentY += 100;
                }

                break;
            case LayoutAlgorithms.TreeHorizontal:
                double currentX = 100;
                foreach (var rootId in roots)
                {
                    currentX = LayoutTreeHorizontal(rootId, currentX, 100, children, visited, pos, metrics);
                    currentX += 100;
                }

                break;
            case LayoutAlgorithms.Radial:
                foreach (var rootId in roots)
                    LayoutRadial(rootId, LayoutRadialCenterX, LayoutRadialCenterY, 0, radialRadiusStep, 0, Math.Tau, children, visited, pos);

                break;
            default:
                return;
        }

        foreach (var node in mindmap.Nodes)
        {
            if (!pos.TryGetValue(node.Id, out var xy))
                continue;
            if (!mindmap.Layout.Nodes.TryGetValue(node.Id, out var layout))
            {
                layout = new NodeLayout();
                mindmap.Layout.Nodes[node.Id] = layout;
            }

            layout.X = xy.X;
            layout.Y = xy.Y;
        }

        mindmap.Layout.Algorithm = algorithm;
    }

    private static bool IsKnownAlgorithm(string algorithm) =>
        algorithm is LayoutAlgorithms.TreeVertical or LayoutAlgorithms.TreeHorizontal or LayoutAlgorithms.Radial;

    private static Func<string, (double W, double H)> BuildMetrics(Mindmap mindmap)
    {
        return id =>
        {
            if (!mindmap.Layout.Nodes.TryGetValue(id, out var L))
                return (DefaultWidth, DefaultHeight);
            var w = L.Width ?? DefaultWidth;
            var h = L.Height ?? DefaultHeight;
            return (w, h);
        };
    }

    private static Dictionary<string, List<string>> GetHierarchyChildren(Mindmap mindmap)
    {
        var nodeIds = mindmap.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in mindmap.Edges.Where(e => e.Kind == MindmapEdgeKind.Hierarchy))
        {
            if (!nodeIds.Contains(e.FromId) || !nodeIds.Contains(e.ToId))
                continue;
            if (!children.TryGetValue(e.FromId, out var list))
            {
                list = new List<string>();
                children[e.FromId] = list;
            }

            list.Add(e.ToId);
        }

        foreach (var list in children.Values)
            list.Sort(StringComparer.Ordinal);

        return children;
    }

    private static List<string> FindRoots(Mindmap mindmap)
    {
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in mindmap.Edges.Where(e => e.Kind == MindmapEdgeKind.Hierarchy))
        {
            if (mindmap.Nodes.Any(n => n.Id == e.ToId))
                incoming.Add(e.ToId);
        }

        var roots = mindmap.Nodes.Select(n => n.Id).Where(id => !incoming.Contains(id)).ToList();
        if (roots.Count == 0 && mindmap.Nodes.Count > 0)
            roots.Add(mindmap.Nodes[0].Id);
        return roots;
    }

    private static double LayoutTreeVertical(
        string nodeId,
        double x,
        double y,
        IReadOnlyDictionary<string, List<string>> children,
        HashSet<string> visited,
        Dictionary<string, (double X, double Y)> pos,
        Func<string, (double W, double H)> metrics)
    {
        if (visited.Contains(nodeId))
            return y;
        visited.Add(nodeId);
        pos[nodeId] = (x, y);
        var (nw, nh) = metrics(nodeId);
        if (!children.TryGetValue(nodeId, out var childList) || childList.Count == 0)
            return y + LayoutTreeVerticalVSpacing + Math.Max(0, nh - DefaultHeight);

        double childX = x + LayoutTreeVerticalHSpacing + Math.Max(0, nw - DefaultWidth);
        double childY = y;
        double firstChildY = y;
        double lastChildY = y;
        foreach (var child in childList)
        {
            lastChildY = childY;
            childY = LayoutTreeVertical(child, childX, childY, children, visited, pos, metrics);
        }

        var p = pos[nodeId];
        pos[nodeId] = (p.X, (firstChildY + lastChildY) / 2);
        return childY;
    }

    private static double LayoutTreeHorizontal(
        string nodeId,
        double x,
        double y,
        IReadOnlyDictionary<string, List<string>> children,
        HashSet<string> visited,
        Dictionary<string, (double X, double Y)> pos,
        Func<string, (double W, double H)> metrics)
    {
        if (visited.Contains(nodeId))
            return x;
        visited.Add(nodeId);
        pos[nodeId] = (x, y);
        var (nw, nh) = metrics(nodeId);
        if (!children.TryGetValue(nodeId, out var childList) || childList.Count == 0)
            return x + LayoutTreeHorizontalHSpacing + Math.Max(0, nw - DefaultWidth);

        double childY = y + LayoutTreeHorizontalVSpacing + Math.Max(0, nh - DefaultHeight);
        double childX = x;
        double firstChildX = x;
        double lastChildX = x;
        foreach (var child in childList)
        {
            lastChildX = childX;
            childX = LayoutTreeHorizontal(child, childX, childY, children, visited, pos, metrics);
        }

        var p = pos[nodeId];
        pos[nodeId] = ((firstChildX + lastChildX) / 2, p.Y);
        return childX;
    }

    private static void LayoutRadial(
        string nodeId,
        double cx,
        double cy,
        int level,
        double radiusStep,
        double angleStart,
        double angleEnd,
        IReadOnlyDictionary<string, List<string>> children,
        HashSet<string> visited,
        Dictionary<string, (double X, double Y)> pos)
    {
        if (visited.Contains(nodeId))
            return;
        visited.Add(nodeId);
        double r = level * radiusStep;
        double angle = (angleStart + angleEnd) / 2;
        pos[nodeId] = (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        if (!children.TryGetValue(nodeId, out var childList) || childList.Count == 0)
            return;
        double slice = (angleEnd - angleStart) / childList.Count;
        for (int i = 0; i < childList.Count; i++)
        {
            double a0 = angleStart + i * slice;
            double a1 = angleStart + (i + 1) * slice;
            LayoutRadial(childList[i], cx, cy, level + 1, radiusStep, a0, a1, children, visited, pos);
        }
    }
}
