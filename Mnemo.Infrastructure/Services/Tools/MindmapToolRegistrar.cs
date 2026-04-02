using Mnemo.Core.Models;
using Mnemo.Core.Models.Tools.Mindmap;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public static class MindmapToolRegistrar
{
    public static void Register(IFunctionRegistry registry, MindmapToolService svc)
    {
        registry.RegisterTool(new AIToolDefinition("list_mindmaps", "Lists mindmaps. Optional title search: keywords (OR); fuzzy (default true) for typos; limit.",
            typeof(ListMindmapsParameters), async args => await svc.ListMindmapsAsync((ListMindmapsParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("read_mindmap", "Nodes (with layout), edges, counts.",
            typeof(MindmapIdParameters), async args => await svc.ReadMindmapAsync((MindmapIdParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("mindmap_exists", "Checks whether a mindmap id exists without loading full summary.",
            typeof(MindmapIdParameters), async args => await svc.MindmapExistsAsync((MindmapIdParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("create_mindmap", "Creates a mindmap with optional root label.",
            typeof(CreateMindmapParameters), async args => await svc.CreateMindmapAsync((CreateMindmapParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("add_node", "Adds a text node; optional parent creates hierarchy edge.",
            typeof(AddNodeParameters), async args => await svc.AddNodeAsync((AddNodeParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("connect_nodes", "Creates a link edge between two nodes.",
            typeof(ConnectNodesParameters), async args => await svc.ConnectNodesAsync((ConnectNodesParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("style_mindmap_node", "Updates node color (hex or default), shape (rectangle/pill/circle), and/or collapsed.",
            typeof(StyleMindmapNodeParameters), async args => await svc.StyleMindmapNodeAsync((StyleMindmapNodeParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("style_mindmap_subtree", "Styles all hierarchy descendants of anchor_node_id (children and deeper). Optional include_anchor.",
            typeof(StyleMindmapSubtreeParameters), async args => await svc.StyleMindmapSubtreeAsync((StyleMindmapSubtreeParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("style_mindmap_edge", "Sets edge visual type: solid, dashed, dotted, double, arrow, bidirect.",
            typeof(StyleMindmapEdgeParameters), async args => await svc.StyleMindmapEdgeAsync((StyleMindmapEdgeParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("apply_mindmap_layout", "Runs auto-layout (TreeVertical, TreeHorizontal, Radial). Optional layout_algorithm defaults to saved.",
            typeof(ApplyMindmapLayoutParameters), async args => await svc.ApplyMindmapLayoutAsync((ApplyMindmapLayoutParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("open_mindmap", "Opens the mindmap editor for an id.",
            typeof(MindmapIdParameters), async args => await svc.OpenMindmapAsync((MindmapIdParameters)args).ConfigureAwait(false)));
    }
}
