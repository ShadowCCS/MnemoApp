using System.Collections.Generic;

namespace Mnemo.Core.Models.Mindmap;

public class MindmapLayout
{
    public string Algorithm { get; set; } = "Freeform";
    public Dictionary<string, NodeLayout> Nodes { get; set; } = new();
}

public class NodeLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}
