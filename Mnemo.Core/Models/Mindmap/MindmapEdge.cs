using System;

namespace Mnemo.Core.Models.Mindmap;

public class MindmapEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public MindmapEdgeKind Kind { get; set; } = MindmapEdgeKind.Hierarchy;
    /// <summary>Visual style: solid, dashed, dotted, double, arrow, bidirect.</summary>
    public string Type { get; set; } = EdgeTypes.Solid;
    public string? Label { get; set; }
}

public static class EdgeTypes
{
    public const string Solid = "solid";
    public const string Dashed = "dashed";
    public const string Dotted = "dotted";
    public const string Double = "double";
    public const string Arrow = "arrow";
    public const string Bidirect = "bidirect";
    public static readonly string[] All = { Solid, Dashed, Dotted, Double, Arrow, Bidirect };
}
