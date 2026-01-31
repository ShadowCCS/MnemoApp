using System;

namespace Mnemo.Core.Models.Mindmap;

public class MindmapEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public MindmapEdgeKind Kind { get; set; } = MindmapEdgeKind.Hierarchy;
    public string? Label { get; set; }
}
