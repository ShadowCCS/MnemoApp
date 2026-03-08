using System;
using System.Threading.Tasks;
using Mnemo.Core.History;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Captures a batched text edit within a single block (typing session).
/// Lighter weight than DocumentOperation since only one block's content changes.
/// </summary>
public class TextEditOperation : IHistoryOperation
{
    private readonly string _blockId;
    private readonly string _textBefore;
    private readonly string _textAfter;
    private readonly CaretState? _caretBefore;
    private readonly CaretState? _caretAfter;
    private readonly Action<string, string, CaretState?> _restoreText;

    public string Description { get; }
    public OperationSource Source => OperationSource.NotesEditor;

    /// <param name="restoreText">Callback: (blockId, newText, caretState) => apply to live document.</param>
    public TextEditOperation(
        string description,
        string blockId,
        string textBefore,
        string textAfter,
        CaretState? caretBefore,
        CaretState? caretAfter,
        Action<string, string, CaretState?> restoreText)
    {
        Description = description;
        _blockId = blockId;
        _textBefore = textBefore;
        _textAfter = textAfter;
        _caretBefore = caretBefore;
        _caretAfter = caretAfter;
        _restoreText = restoreText;
    }

    public Task ApplyAsync()
    {
        _restoreText(_blockId, _textAfter, _caretAfter);
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        _restoreText(_blockId, _textBefore, _caretBefore);
        return Task.CompletedTask;
    }
}
