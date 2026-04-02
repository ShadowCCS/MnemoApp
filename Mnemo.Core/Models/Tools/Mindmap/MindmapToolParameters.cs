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

public sealed class CreateMindmapParameters
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("root_label")] public string? RootLabel { get; set; }
}

public sealed class AddNodeParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("parent_node_id")] public string? ParentNodeId { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
}

public sealed class ConnectNodesParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("source_node_id")] public string SourceNodeId { get; set; } = string.Empty;
    [JsonPropertyName("target_node_id")] public string TargetNodeId { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string? Label { get; set; }
}

public sealed class StyleMindmapNodeParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("node_id")] public string NodeId { get; set; } = string.Empty;
    /// <summary>Hex color (e.g. #e95b45) or <c>default</c> / empty to clear and use theme default.</summary>
    [JsonPropertyName("color")] public string? Color { get; set; }
    /// <summary><c>rectangle</c>, <c>pill</c>, or <c>circle</c>.</summary>
    [JsonPropertyName("shape")] public string? Shape { get; set; }
    /// <summary>When set, persists subtree collapsed state (hierarchy hide).</summary>
    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }
}

public sealed class StyleMindmapSubtreeParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    /// <summary>Hub node: all nodes reachable via outgoing hierarchy edges from this id are styled (default: not the anchor itself).</summary>
    [JsonPropertyName("anchor_node_id")] public string AnchorNodeId { get; set; } = string.Empty;
    /// <summary>When true, also applies the same style updates to the anchor node.</summary>
    [JsonPropertyName("include_anchor")] public bool? IncludeAnchor { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("shape")] public string? Shape { get; set; }
    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }
}

public sealed class StyleMindmapEdgeParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    [JsonPropertyName("edge_id")] public string EdgeId { get; set; } = string.Empty;
    /// <summary>One of: solid, dashed, dotted, double, arrow, bidirect.</summary>
    [JsonPropertyName("edge_type")] public string EdgeType { get; set; } = string.Empty;
}

public sealed class ApplyMindmapLayoutParameters
{
    [JsonPropertyName("mindmap_id")] public string MindmapId { get; set; } = string.Empty;
    /// <summary>Optional. One of: TreeVertical, TreeHorizontal, Radial. When omitted, uses the mindmap's saved algorithm.</summary>
    [JsonPropertyName("layout_algorithm")] public string? LayoutAlgorithm { get; set; }
}
