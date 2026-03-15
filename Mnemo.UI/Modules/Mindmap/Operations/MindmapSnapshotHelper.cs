using System.Text.Json;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;

namespace Mnemo.UI.Modules.Mindmap.Operations;

public static class MindmapSnapshotHelper
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Deep clone of the mindmap for undo/redo snapshots.</summary>
    public static MindmapModel Clone(MindmapModel source)
    {
        var json = JsonSerializer.Serialize(source, s_options);
        return JsonSerializer.Deserialize<MindmapModel>(json, s_options)!;
    }
}
