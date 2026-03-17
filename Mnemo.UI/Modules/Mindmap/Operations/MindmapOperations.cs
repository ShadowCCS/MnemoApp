using System;
using System.Threading.Tasks;
using Mnemo.Core.History;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;

namespace Mnemo.UI.Modules.Mindmap.Operations;

/// <summary>
/// Full mindmap state snapshot for undo/redo. Apply = restore "after" state; Rollback = restore "before" state.
/// </summary>
public class MindmapStateOperation : IHistoryOperation
{
    private readonly MindmapModel _before;
    private readonly MindmapModel _after;
    private readonly Func<MindmapModel, Task> _restore;

    public MindmapStateOperation(string description, MindmapModel before, MindmapModel after, Func<MindmapModel, Task> restore)
    {
        Description = description;
        _before = before;
        _after = after;
        _restore = restore;
    }

    public string Description { get; }
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => _restore(_after);
    public Task RollbackAsync() => _restore(_before);
}
