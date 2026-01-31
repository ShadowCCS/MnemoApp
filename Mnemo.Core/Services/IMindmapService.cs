using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Mindmap;

namespace Mnemo.Core.Services;

public interface IMindmapService
{
    Task<Result<IEnumerable<Mindmap>>> GetAllMindmapsAsync();
    Task<Result<Mindmap>> GetMindmapAsync(string id);
    Task<Result<Mindmap>> CreateMindmapAsync(string title);
    Task<Result> SaveMindmapAsync(Mindmap mindmap);
    Task<Result> DeleteMindmapAsync(string id);

    // Graph operations
    Task<Result<MindmapNode>> AddNodeAsync(string mindmapId, string nodeType, IMindmapNodeContent content, double x, double y);
    Task<Result> RemoveNodeAsync(string mindmapId, string nodeId);
    Task<Result<MindmapEdge>> AddEdgeAsync(string mindmapId, string fromId, string toId, MindmapEdgeKind kind, string? label = null);
    Task<Result> RemoveEdgeAsync(string mindmapId, string edgeId);
    Task<Result> UpdateNodeContentAsync(string mindmapId, string nodeId, IMindmapNodeContent content);
    Task<Result> UpdateNodeLayoutAsync(string mindmapId, string nodeId, double x, double y, double? width = null, double? height = null);
    
    // Integrity
    bool WouldCreateCycle(Mindmap mindmap, string fromId, string toId);
}
