using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models.Mindmap;

public class MindmapNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NodeType { get; set; } = "text";
    public IMindmapNodeContent Content { get; set; } = new TextNodeContent();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public Dictionary<string, string> Style { get; set; } = new();
}
