using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.History;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Captures a batched text edit within a single block (typing session).
/// Stores inline spans so formatting is preserved across undo/redo.
/// </summary>
public class TextEditOperation : IHistoryOperation
{
    private readonly string _blockId;
    private readonly List<InlineSpan> _spansBefore;
    private readonly List<InlineSpan> _spansAfter;
    private readonly CaretState? _caretBefore;
    private readonly CaretState? _caretAfter;
    private readonly Action<string, List<InlineSpan>, CaretState?> _restoreSpans;

    public string Description { get; }
    public OperationSource Source => OperationSource.NotesEditor;

    /// <param name="restoreSpans">Callback: (blockId, spans, caretState) => apply to live document.</param>
    public TextEditOperation(
        string description,
        string blockId,
        List<InlineSpan> spansBefore,
        List<InlineSpan> spansAfter,
        CaretState? caretBefore,
        CaretState? caretAfter,
        Action<string, List<InlineSpan>, CaretState?> restoreSpans)
    {
        Description = description;
        _blockId = blockId;
        _spansBefore = new List<InlineSpan>(spansBefore);
        _spansAfter = new List<InlineSpan>(spansAfter);
        _caretBefore = caretBefore;
        _caretAfter = caretAfter;
        _restoreSpans = restoreSpans;
    }

    public Task ApplyAsync()
    {
        _restoreSpans(_blockId, _spansAfter, _caretAfter);
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        _restoreSpans(_blockId, _spansBefore, _caretBefore);
        return Task.CompletedTask;
    }
}
