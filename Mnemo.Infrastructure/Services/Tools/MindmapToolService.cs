using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Mindmap;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public sealed class MindmapToolService
{
    private readonly IMindmapService _mindmaps;
    private readonly INavigationService _nav;
    private readonly IMainThreadDispatcher _ui;

    public MindmapToolService(IMindmapService mindmaps, INavigationService nav, IMainThreadDispatcher ui)
    {
        _mindmaps = mindmaps;
        _nav = nav;
        _ui = ui;
    }

    public async Task<ToolInvocationResult> ListMindmapsAsync(ListMindmapsParameters p)
    {
        var res = await _mindmaps.GetAllMindmapsAsync().ConfigureAwait(false);
        if (!res.IsSuccess || res.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Failed to list mindmaps.");

        var limit = p.Limit is > 0 and <= 100 ? p.Limit!.Value : 50;
        var q = p.Search?.Trim();
        var list = res.Value;
        if (!string.IsNullOrEmpty(q))
            list = list.Where(m => m.Title.Contains(q, StringComparison.OrdinalIgnoreCase));

        var slice = list.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase).Take(limit)
            .Select(m => new { mindmap_id = m.Id, title = m.Title }).ToList();

        return ToolInvocationResult.Success($"Mindmaps: {slice.Count}", new { mindmaps = slice });
    }

    public async Task<ToolInvocationResult> ReadMindmapAsync(MindmapIdParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");

        var res = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!res.IsSuccess || res.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var m = res.Value;
        var nodes = m.Nodes.Select(n =>
        {
            object? layout = null;
            if (m.Layout.Nodes.TryGetValue(n.Id, out var pl))
                layout = new { x = pl.X, y = pl.Y, width = pl.Width, height = pl.Height };

            n.Style.TryGetValue("color", out var color);
            n.Style.TryGetValue("shape", out var shape);
            var collapsed = n.Style.TryGetValue("collapsed", out var coll) && coll == "true";
            return new
            {
                node_id = n.Id,
                n.NodeType,
                text = n.Content is TextNodeContent t ? t.Text : n.Content?.ToString(),
                color,
                shape,
                collapsed,
                layout
            };
        }).ToList();
        var edges = m.Edges.Select(e => new
        {
            edge_id = e.Id,
            e.FromId,
            e.ToId,
            kind = e.Kind.ToString(),
            type = string.IsNullOrWhiteSpace(e.Type) ? EdgeTypes.Solid : e.Type,
            e.Label
        }).ToList();

        var algo = m.Layout.Algorithm;
        if (string.IsNullOrWhiteSpace(algo) || string.Equals(algo, "Freeform", StringComparison.Ordinal))
            algo = LayoutAlgorithms.TreeVertical;

        return ToolInvocationResult.Success("Mindmap summary.", new
        {
            mindmap_id = m.Id,
            title = m.Title,
            root_node_id = m.RootNodeId,
            layout_algorithm = algo,
            node_count = nodes.Count,
            edge_count = edges.Count,
            nodes,
            edges
        });
    }

    public async Task<ToolInvocationResult> MindmapExistsAsync(MindmapIdParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");

        var id = p.MindmapId.Trim();
        var res = await _mindmaps.GetMindmapAsync(id).ConfigureAwait(false);
        if (!res.IsSuccess || res.Value == null)
            return ToolInvocationResult.Success("Mindmap does not exist.", new { mindmap_id = id, exists = false });

        var m = res.Value;
        return ToolInvocationResult.Success("Mindmap exists.", new
        {
            mindmap_id = m.Id,
            exists = true,
            title = m.Title,
            node_count = m.Nodes.Count,
            edge_count = m.Edges.Count
        });
    }

    public async Task<ToolInvocationResult> CreateMindmapAsync(CreateMindmapParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Title))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "title is required.");

        var res = await _mindmaps.CreateMindmapAsync(p.Title.Trim()).ConfigureAwait(false);
        if (!res.IsSuccess || res.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Create failed.");

        var m = res.Value;
        if (!string.IsNullOrWhiteSpace(p.RootLabel) && m.RootNodeId != null)
        {
            var root = m.Nodes.FirstOrDefault(n => n.Id == m.RootNodeId);
            if (root?.Content is TextNodeContent tc)
            {
                tc.Text = p.RootLabel.Trim();
                await _mindmaps.SaveMindmapAsync(m).ConfigureAwait(false);
            }
        }

        return ToolInvocationResult.Success($"Mindmap created (id: {m.Id})", new { mindmap_id = m.Id, title = m.Title });
    }

    public async Task<ToolInvocationResult> AddNodeAsync(AddNodeParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId) || string.IsNullOrWhiteSpace(p.Label))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id and label are required.");

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;
        if (!string.IsNullOrWhiteSpace(p.ParentNodeId))
        {
            var pid = p.ParentNodeId.Trim();
            if (mindmap.Nodes.All(n => n.Id != pid))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"parent_node_id not found: {pid}.");
        }

        double x = 420, y = 320;
        if (!string.IsNullOrWhiteSpace(p.ParentNodeId) &&
            mindmap.Layout.Nodes.TryGetValue(p.ParentNodeId.Trim(), out var pl))
        {
            x = pl.X + 120;
            y = pl.Y;
        }

        var content = new TextNodeContent { Text = p.Label.Trim() };
        var nodeRes = await _mindmaps.AddNodeAsync(mindmap.Id, "text", content, x, y).ConfigureAwait(false);
        if (!nodeRes.IsSuccess || nodeRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, nodeRes.ErrorMessage ?? "Add node failed.");

        if (!string.IsNullOrWhiteSpace(p.ParentNodeId))
        {
            var edge = await _mindmaps
                .AddEdgeAsync(mindmap.Id, p.ParentNodeId.Trim(), nodeRes.Value.Id, MindmapEdgeKind.Hierarchy)
                .ConfigureAwait(false);
            if (!edge.IsSuccess)
                return ToolInvocationResult.Success($"Node added but hierarchy edge failed: {edge.ErrorMessage}",
                    new { node_id = nodeRes.Value.Id });
        }

        return ToolInvocationResult.Success("Node added.", new { node_id = nodeRes.Value.Id });
    }

    public async Task<ToolInvocationResult> ConnectNodesAsync(ConnectNodesParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId) ||
            string.IsNullOrWhiteSpace(p.SourceNodeId) ||
            string.IsNullOrWhiteSpace(p.TargetNodeId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id, source_node_id, target_node_id required.");

        var edgeRes = await _mindmaps.AddEdgeAsync(
            p.MindmapId.Trim(),
            p.SourceNodeId.Trim(),
            p.TargetNodeId.Trim(),
            MindmapEdgeKind.Link,
            p.Label).ConfigureAwait(false);

        if (!edgeRes.IsSuccess || edgeRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, edgeRes.ErrorMessage ?? "Connect failed.");

        return ToolInvocationResult.Success("Connected.", new { edge_id = edgeRes.Value.Id });
    }

    public async Task<ToolInvocationResult> StyleMindmapNodeAsync(StyleMindmapNodeParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId) || string.IsNullOrWhiteSpace(p.NodeId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id and node_id are required.");

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;
        var nid = p.NodeId.Trim();
        if (mindmap.Nodes.All(n => n.Id != nid))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"node_id not found: {nid}");

        var buildErr = TryBuildMindmapNodeStyleUpdates(p.Color, p.Shape, p.Collapsed, out var updates);
        if (buildErr != null)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, buildErr);

        var res = await _mindmaps.UpdateNodeStyleAsync(mindmap.Id, nid, updates).ConfigureAwait(false);
        if (!res.IsSuccess)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Update node style failed.");

        return ToolInvocationResult.Success("Node style updated.");
    }

    public async Task<ToolInvocationResult> StyleMindmapSubtreeAsync(StyleMindmapSubtreeParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId) || string.IsNullOrWhiteSpace(p.AnchorNodeId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id and anchor_node_id are required.");

        var buildErr = TryBuildMindmapNodeStyleUpdates(p.Color, p.Shape, p.Collapsed, out var updates);
        if (buildErr != null)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, buildErr);

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;
        var anchor = p.AnchorNodeId.Trim();
        if (mindmap.Nodes.All(n => n.Id != anchor))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"anchor_node_id not found: {anchor}");

        var targets = new HashSet<string>(CollectHierarchyDescendantNodeIds(mindmap, anchor), StringComparer.Ordinal);
        if (p.IncludeAnchor == true)
            targets.Add(anchor);

        if (targets.Count == 0)
            return ToolInvocationResult.Success("No hierarchy descendants to style (only links or empty branch).", new { updated_count = 0 });

        foreach (var nid in targets.OrderBy(x => x, StringComparer.Ordinal))
        {
            var res = await _mindmaps.UpdateNodeStyleAsync(mindmap.Id, nid, updates).ConfigureAwait(false);
            if (!res.IsSuccess)
                return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? $"Update node style failed for {nid}.");
        }

        return ToolInvocationResult.Success($"Subtree style applied to {targets.Count} node(s).", new { updated_count = targets.Count });
    }

    /// <summary>Builds the style patch dict for <see cref="IMindmapService.UpdateNodeStyleAsync"/>. Returns null on success, else a validation message.</summary>
    private static string? TryBuildMindmapNodeStyleUpdates(string? color, string? shape, bool? collapsed, out Dictionary<string, string?> updates)
    {
        updates = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (color != null)
        {
            var c = color.Trim();
            if (c.Length == 0 || string.Equals(c, "default", StringComparison.OrdinalIgnoreCase))
                updates["color"] = null;
            else
                updates["color"] = c;
        }

        if (shape != null)
        {
            var s = shape.Trim().ToLowerInvariant();
            if (s is not ("rectangle" or "pill" or "circle"))
                return "shape must be rectangle, pill, or circle.";

            updates["shape"] = s;
        }

        if (collapsed.HasValue)
            updates["collapsed"] = collapsed.Value ? "true" : null;

        return updates.Count == 0 ? "Provide at least one of: color, shape, collapsed." : null;
    }

    /// <summary>All node ids reachable from <paramref name="anchorNodeId"/> following outgoing hierarchy edges (excluding the anchor).</summary>
    private static IEnumerable<string> CollectHierarchyDescendantNodeIds(Mindmap mindmap, string anchorNodeId)
    {
        var validIds = mindmap.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var e in mindmap.Edges)
        {
            if (e.Kind != MindmapEdgeKind.Hierarchy || e.FromId != anchorNodeId)
                continue;
            if (!validIds.Contains(e.ToId) || !visited.Add(e.ToId))
                continue;
            queue.Enqueue(e.ToId);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            yield return id;
            foreach (var e in mindmap.Edges)
            {
                if (e.Kind != MindmapEdgeKind.Hierarchy || e.FromId != id)
                    continue;
                if (!validIds.Contains(e.ToId) || !visited.Add(e.ToId))
                    continue;
                queue.Enqueue(e.ToId);
            }
        }
    }

    public async Task<ToolInvocationResult> StyleMindmapEdgeAsync(StyleMindmapEdgeParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId) || string.IsNullOrWhiteSpace(p.EdgeId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id and edge_id are required.");

        var t = p.EdgeType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(t) || Array.IndexOf(EdgeTypes.All, t) < 0)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                $"edge_type must be one of: {string.Join(", ", EdgeTypes.All)}.");

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;
        var eid = p.EdgeId.Trim();
        if (mindmap.Edges.All(e => e.Id != eid))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"edge_id not found: {eid}");

        var res = await _mindmaps.UpdateEdgeTypeAsync(mindmap.Id, eid, t).ConfigureAwait(false);
        if (!res.IsSuccess)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Update edge style failed.");

        return ToolInvocationResult.Success("Edge style updated.");
    }

    public async Task<ToolInvocationResult> ApplyMindmapLayoutAsync(ApplyMindmapLayoutParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;
        var algo = p.LayoutAlgorithm?.Trim();
        if (!string.IsNullOrEmpty(algo)
            && algo != LayoutAlgorithms.TreeVertical
            && algo != LayoutAlgorithms.TreeHorizontal
            && algo != LayoutAlgorithms.Radial)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                "layout_algorithm must be TreeVertical, TreeHorizontal, or Radial.");

        if (string.IsNullOrEmpty(algo))
            algo = mindmap.Layout.Algorithm;

        global::Mnemo.Infrastructure.Services.MindmapGraphLayout.Apply(mindmap, algo!);
        var save = await _mindmaps.SaveMindmapAsync(mindmap).ConfigureAwait(false);
        if (!save.IsSuccess)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, save.ErrorMessage ?? "Save failed.");

        return ToolInvocationResult.Success($"Layout applied ({mindmap.Layout.Algorithm}).");
    }

    public async Task<ToolInvocationResult> OpenMindmapAsync(MindmapIdParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");

        var res = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!res.IsSuccess || res.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var id = p.MindmapId.Trim();
        await _ui.InvokeAsync(() =>
        {
            _nav.NavigateTo("mindmap-detail", id);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return ToolInvocationResult.Success($"Opened mindmap \"{res.Value.Title}\".");
    }
}
