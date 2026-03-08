using System;
using System.Threading.Tasks;
using Mnemo.Core.History;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Whole-document snapshot operation. Captures the full block list before and after
/// a mutation so undo/redo can restore any structural change (insert, delete, merge,
/// move, type-change, paste, multi-block delete, etc.) in one step.
/// </summary>
public class DocumentOperation : IHistoryOperation
{
    private readonly BlockSnapshot[] _before;
    private readonly BlockSnapshot[] _after;
    private readonly CaretState? _caretBefore;
    private readonly CaretState? _caretAfter;
    private readonly Action<Block[], CaretState?> _restore;

    public string Description { get; }
    public OperationSource Source => OperationSource.NotesEditor;

    public DocumentOperation(
        string description,
        BlockSnapshot[] before,
        BlockSnapshot[] after,
        CaretState? caretBefore,
        CaretState? caretAfter,
        Action<Block[], CaretState?> restore)
    {
        Description = description;
        _before = before;
        _after = after;
        _caretBefore = caretBefore;
        _caretAfter = caretAfter;
        _restore = restore;
    }

    public Task ApplyAsync()
    {
        var blocks = new Block[_after.Length];
        for (int i = 0; i < _after.Length; i++)
            blocks[i] = _after[i].ToBlock();
        _restore(blocks, _caretAfter);
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        var blocks = new Block[_before.Length];
        for (int i = 0; i < _before.Length; i++)
            blocks[i] = _before[i].ToBlock();
        _restore(blocks, _caretBefore);
        return Task.CompletedTask;
    }
}
