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
        var fuzzy = p.Fuzzy ?? true;
        var list = res.Value;
        if (!string.IsNullOrEmpty(q))
        {
            list = list.Where(m =>
            {
                var title = m.Title ?? string.Empty;
                if (title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    return true;

                var tokens = TextSearchMatch.ResolveSearchTokens(q);
                if (tokens.Count == 0)
                    return false;
                if (tokens.Count == 1)
                    return TextSearchMatch.MatchTokens(title, tokens, matchAll: true, fuzzy);
                return TextSearchMatch.MatchTokens(title, tokens, matchAll: false, fuzzy);
            });
        }

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

        if (p.Nodes is { Count: > 0 })
        {
            var batch = await AddNodesAsync(new AddNodesParameters { MindmapId = m.Id, Nodes = p.Nodes }).ConfigureAwait(false);
            if (!batch.Ok)
                return batch;
        }

        return ToolInvocationResult.Success($"Mindmap created (id: {m.Id})", new { mindmap_id = m.Id, title = m.Title });
    }

    public async Task<ToolInvocationResult> AddNodesAsync(AddNodesParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");
        if (p.Nodes == null || p.Nodes.Count == 0)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "nodes must be a non-empty array.");

        var created = new List<string>();
        for (var i = 0; i < p.Nodes.Count; i++)
        {
            var item = p.Nodes[i];
            if (string.IsNullOrWhiteSpace(item.Label))
                return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"nodes[{i}].label is required.");

            var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
            if (!mapRes.IsSuccess || mapRes.Value == null)
                return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

            var mindmap = mapRes.Value;
            var rootId = mindmap.RootNodeId;
            if (string.IsNullOrWhiteSpace(rootId))
                return ToolInvocationResult.Failure(ToolResultCodes.InternalError, "Mindmap has no root node.");

            string? resolvedParent;
            if (!string.IsNullOrWhiteSpace(item.ParentNodeId))
            {
                resolvedParent = item.ParentNodeId.Trim();
                if (mindmap.Nodes.All(n => n.Id != resolvedParent))
                    return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"parent_node_id not found: {resolvedParent}");
            }
            else if (item.ParentIndex.HasValue)
            {
                var pi = item.ParentIndex.Value;
                if (pi < 0 || pi >= created.Count)
                    return ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                        $"nodes[{i}].parent_index must reference an earlier entry in this batch (0..{created.Count - 1}).");
                resolvedParent = created[pi];
            }
            else
                resolvedParent = rootId;

            double x = 420, y = 320;
            if (!string.IsNullOrWhiteSpace(resolvedParent) &&
                mindmap.Layout.Nodes.TryGetValue(resolvedParent!, out var pl))
            {
                x = pl.X + 120;
                y = pl.Y;
            }

            var content = new TextNodeContent { Text = item.Label.Trim() };
            var nodeRes = await _mindmaps.AddNodeAsync(mindmap.Id, "text", content, x, y).ConfigureAwait(false);
            if (!nodeRes.IsSuccess || nodeRes.Value == null)
                return ToolInvocationResult.Failure(ToolResultCodes.InternalError, nodeRes.ErrorMessage ?? "Add node failed.");

            var newId = nodeRes.Value.Id;
            if (!string.IsNullOrWhiteSpace(resolvedParent))
            {
                var edge = await _mindmaps
                    .AddEdgeAsync(mindmap.Id, resolvedParent!, newId, MindmapEdgeKind.Hierarchy)
                    .ConfigureAwait(false);
                if (!edge.IsSuccess)
                    return ToolInvocationResult.Failure(ToolResultCodes.InternalError,
                        $"nodes[{i}]: hierarchy edge failed: {edge.ErrorMessage}");
            }

            if (item.Color != null || item.Shape != null)
            {
                var styleErr = TryBuildMindmapNodeStyleUpdates(item.Color, item.Shape, null, out var styleUpdates);
                if (styleErr != null)
                    return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, styleErr);
                if (styleUpdates.Count > 0)
                {
                    var sr = await _mindmaps.UpdateNodeStyleAsync(mindmap.Id, newId, styleUpdates).ConfigureAwait(false);
                    if (!sr.IsSuccess)
                        return ToolInvocationResult.Failure(ToolResultCodes.InternalError, sr.ErrorMessage ?? "Node style failed.");
                }
            }

            created.Add(newId);
        }

        return ToolInvocationResult.Success($"Added {created.Count} node(s).", new { node_ids = created });
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

    public async Task<ToolInvocationResult> StyleNodesAsync(StyleNodesParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.MindmapId))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "mindmap_id is required.");

        var hasIds = p.NodeIds is { Count: > 0 };
        var hasSubtree = !string.IsNullOrWhiteSpace(p.SubtreeOf);
        if (!hasIds && !hasSubtree)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "Provide node_ids or subtree_of.");
        if (hasIds && hasSubtree)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "Provide only one of node_ids or subtree_of.");

        var buildErr = TryBuildMindmapNodeStyleUpdates(p.Color, p.Shape, p.Collapsed, out var updates);
        if (buildErr != null)
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, buildErr);

        var mapRes = await _mindmaps.GetMindmapAsync(p.MindmapId.Trim()).ConfigureAwait(false);
        if (!mapRes.IsSuccess || mapRes.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Mindmap not found.");

        var mindmap = mapRes.Value;

        if (hasIds)
        {
            var n = 0;
            foreach (var raw in p.NodeIds!)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var nid = raw.Trim();
                if (mindmap.Nodes.All(x => x.Id != nid))
                    return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"node_id not found: {nid}");

                var res = await _mindmaps.UpdateNodeStyleAsync(mindmap.Id, nid, updates).ConfigureAwait(false);
                if (!res.IsSuccess)
                    return ToolInvocationResult.Failure(ToolResultCodes.InternalError, res.ErrorMessage ?? "Update node style failed.");
                n++;
            }

            return ToolInvocationResult.Success($"Style applied to {n} node(s).", new { updated_count = n });
        }

        var anchor = p.SubtreeOf!.Trim();
        if (mindmap.Nodes.All(x => x.Id != anchor))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, $"subtree_of not found: {anchor}");

        var targets = new HashSet<string>(CollectHierarchyDescendantNodeIds(mindmap, anchor), StringComparer.Ordinal);
        if (p.IncludeAnchor == true)
            targets.Add(anchor);

        if (targets.Count == 0)
            return ToolInvocationResult.Success("No hierarchy descendants to style.", new { updated_count = 0 });

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
