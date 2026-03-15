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

/// <summary>
/// Placeholder for creating one or more nodes with optional edges.
/// Designed for compound operations (add-node-plus-edges is atomic undo).
/// </summary>
public class CreateNodeOperation : IHistoryOperation
{
    public string Description => "Create node";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for deleting one or more nodes and their incidental edges.
/// </summary>
public class DeleteNodesOperation : IHistoryOperation
{
    public string Description => "Delete nodes";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for moving one or more nodes to new positions.
/// Multi-node drag is a compound operation.
/// </summary>
public class MoveNodesOperation : IHistoryOperation
{
    public string Description => "Move nodes";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for connecting two nodes with an edge.
/// </summary>
public class ConnectNodesOperation : IHistoryOperation
{
    public string Description => "Connect nodes";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for detaching (removing) edges between nodes.
/// </summary>
public class DetachEdgesOperation : IHistoryOperation
{
    public string Description => "Detach edges";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for updating a node's text content.
/// </summary>
public class UpdateNodeContentOperation : IHistoryOperation
{
    public string Description => "Update node content";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}

/// <summary>
/// Placeholder for auto-layout. All node position changes are a compound operation.
/// </summary>
public class AutoLayoutOperation : IHistoryOperation
{
    public string Description => "Auto layout";
    public OperationSource Source => OperationSource.MindmapEditor;

    public Task ApplyAsync() => Task.CompletedTask;
    public Task RollbackAsync() => Task.CompletedTask;
}
