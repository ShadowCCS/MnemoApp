using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mnemo.Core.Models.Tools.Mindmap;

public sealed class ListMindmapsParameters
{
    [JsonPropertyName("search")] public string? Search { get; set; }
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("fuzzy")] public bool? Fuzzy { get; set; }
}

public sealed class MindmapIdParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
}

public sealed class MindmapNodeAddItem
{
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    /// <summary>Existing node id as parent (hierarchy edge).</summary>
    [JsonPropertyName("parent_node_id")] public string? ParentNodeId { get; set; }
    /// <summary>0-based index into the same batch's nodes array; parent must appear earlier.</summary>
    [JsonPropertyName("parent_index")] public int? ParentIndex { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("shape")] public string? Shape { get; set; }
}

public sealed class CreateMindmapParameters
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("root_label")] public string? RootLabel { get; set; }
    /// <summary>Optional nodes to add under the new root in one call (uses parent_node_id / parent_index).</summary>
    [JsonPropertyName("nodes")] public List<MindmapNodeAddItem>? Nodes { get; set; }
}

public sealed class AddNodesParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("nodes")] public List<MindmapNodeAddItem> Nodes { get; set; } = [];
}

public sealed class ConnectNodesParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("source_node_id")] public string SourceNodeId { get; set; } = string.Empty;
    [JsonPropertyName("target_node_id")] public string TargetNodeId { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string? Label { get; set; }
}

public sealed class StyleNodesParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("node_ids")] public List<string>? NodeIds { get; set; }
    /// <summary>When set, style all hierarchy descendants of this node (optional include_anchor).</summary>
    [JsonPropertyName("subtree_of")] public string? SubtreeOf { get; set; }
    [JsonPropertyName("include_anchor")] public bool? IncludeAnchor { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("shape")] public string? Shape { get; set; }
    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }
}

public sealed class ApplyMindmapLayoutParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("layout_algorithm")] public string? LayoutAlgorithm { get; set; }
}
