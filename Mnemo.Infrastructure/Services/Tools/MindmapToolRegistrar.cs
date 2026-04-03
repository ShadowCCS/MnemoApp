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
        registry.RegisterTool(new AIToolDefinition("create_mindmap", "Creates a mindmap; optional root_label and optional nodes[] batch under root.",
            typeof(CreateMindmapParameters), async args => await svc.CreateMindmapAsync((CreateMindmapParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("add_nodes", "Adds one or more nodes; parent_node_id or parent_index for hierarchy.",
            typeof(AddNodesParameters), async args => await svc.AddNodesAsync((AddNodesParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("connect_nodes", "Creates a link edge between two nodes.",
            typeof(ConnectNodesParameters), async args => await svc.ConnectNodesAsync((ConnectNodesParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("style_nodes", "Style by node_ids or subtree_of anchor; color, shape, collapsed.",
            typeof(StyleNodesParameters), async args => await svc.StyleNodesAsync((StyleNodesParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("apply_layout", "Runs auto-layout (TreeVertical, TreeHorizontal, Radial).",
            typeof(ApplyMindmapLayoutParameters), async args => await svc.ApplyMindmapLayoutAsync((ApplyMindmapLayoutParameters)args).ConfigureAwait(false)));
        registry.RegisterTool(new AIToolDefinition("open_mindmap", "Opens the mindmap editor for an id.",
            typeof(MindmapIdParameters), async args => await svc.OpenMindmapAsync((MindmapIdParameters)args).ConfigureAwait(false)));
    }
}
