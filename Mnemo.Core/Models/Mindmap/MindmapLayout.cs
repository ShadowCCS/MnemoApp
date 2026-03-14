using System.Collections.Generic;

namespace Mnemo.Core.Models.Mindmap;

/// <summary>Known layout algorithm identifiers. Use these for <see cref="MindmapLayout.Algorithm"/>.</summary>
public static class LayoutAlgorithms
{
    public const string TreeVertical = "TreeVertical";
    public const string TreeHorizontal = "TreeHorizontal";
    public const string Radial = "Radial";
}

public class MindmapLayout
{
    public string Algorithm { get; set; } = LayoutAlgorithms.TreeVertical;
    public Dictionary<string, NodeLayout> Nodes { get; set; } = new();
}

public class NodeLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}
