using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.History;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Captures a batched text edit within a single block (typing session).
/// Stores inline runs so formatting is preserved across undo/redo.
/// </summary>
public class TextEditOperation : IHistoryOperation
{
    private readonly string _blockId;
    private readonly List<InlineRun> _runsBefore;
    private readonly List<InlineRun> _runsAfter;
    private readonly CaretState? _caretBefore;
    private readonly CaretState? _caretAfter;
    private readonly Action<string, List<InlineRun>, CaretState?> _restoreRuns;

    public string Description { get; }
    public OperationSource Source => OperationSource.NotesEditor;

    /// <param name="restoreRuns">Callback: (blockId, runs, caretState) => apply to live document.</param>
    public TextEditOperation(
        string description,
        string blockId,
        List<InlineRun> runsBefore,
        List<InlineRun> runsAfter,
        CaretState? caretBefore,
        CaretState? caretAfter,
        Action<string, List<InlineRun>, CaretState?> restoreRuns)
    {
        Description = description;
        _blockId = blockId;
        _runsBefore = new List<InlineRun>(runsBefore);
        _runsAfter = new List<InlineRun>(runsAfter);
        _caretBefore = caretBefore;
        _caretAfter = caretAfter;
        _restoreRuns = restoreRuns;
    }

    public Task ApplyAsync()
    {
        _restoreRuns(_blockId, _runsAfter, _caretAfter);
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        _restoreRuns(_blockId, _runsBefore, _caretBefore);
        return Task.CompletedTask;
    }
}
