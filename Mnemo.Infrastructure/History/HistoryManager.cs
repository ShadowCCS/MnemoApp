using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Mnemo.Core.History;

namespace Mnemo.Infrastructure.History;

public class HistoryManager : IHistoryManager
{
    private readonly Stack<IHistoryOperation> _undoStack = new();
    private readonly Stack<IHistoryOperation> _redoStack = new();

    private List<IHistoryOperation>? _batchOperations;
    private string? _batchDescription;
    private OperationSource _batchSource;
    private bool _isUndoRedoInProgress;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool IsBatching => _batchOperations != null;

    public event Action? StateChanged;

    [Conditional("DEBUG")]
    private void Log(string msg) => Debug.WriteLine($"[HistoryManager] {msg} | undo={_undoStack.Count} redo={_redoStack.Count}");

    public void Push(IHistoryOperation operation)
    {
        if (_isUndoRedoInProgress)
        {
            Log($"Push BLOCKED (undo/redo in progress): '{operation.Description}'");
            return;
        }

        if (_batchOperations != null)
        {
            _batchOperations.Add(operation);
            Log($"Push to batch: '{operation.Description}'");
            return;
        }

        _undoStack.Push(operation);
        _redoStack.Clear();
        Log($"Push: '{operation.Description}'");
        StateChanged?.Invoke();
    }

    public void BeginBatch(string description, OperationSource source)
    {
        if (_batchOperations != null)
            throw new InvalidOperationException("A batch is already open. Nested batches are not supported.");

        _batchOperations = new List<IHistoryOperation>();
        _batchDescription = description;
        _batchSource = source;
        Log($"BeginBatch: '{description}'");
    }

    public void CommitBatch()
    {
        if (_batchOperations == null) return;

        var ops = _batchOperations;
        var desc = _batchDescription ?? "Batch";
        var source = _batchSource;
        _batchOperations = null;
        _batchDescription = null;

        if (ops.Count == 0)
        {
            Log($"CommitBatch: empty, discarded");
            return;
        }

        if (ops.Count == 1)
        {
            _undoStack.Push(ops[0]);
        }
        else
        {
            _undoStack.Push(new CompositeOperation(desc, source, ops));
        }

        _redoStack.Clear();
        Log($"CommitBatch: '{desc}' with {ops.Count} ops");
        StateChanged?.Invoke();
    }

    public void DiscardBatch()
    {
        _batchOperations = null;
        _batchDescription = null;
    }

    public async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;

        _isUndoRedoInProgress = true;
        try
        {
            var op = _undoStack.Pop();
            Log($"Undo: '{op.Description}'");
            await op.RollbackAsync();
            _redoStack.Push(op);
        }
        finally
        {
            _isUndoRedoInProgress = false;
        }
        Log("Undo complete");
        StateChanged?.Invoke();
    }

    public async Task RedoAsync()
    {
        if (_redoStack.Count == 0) return;

        _isUndoRedoInProgress = true;
        try
        {
            var op = _redoStack.Pop();
            Log($"Redo: '{op.Description}'");
            await op.ApplyAsync();
            _undoStack.Push(op);
        }
        finally
        {
            _isUndoRedoInProgress = false;
        }
        Log("Redo complete");
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        Log("Clear");
        _undoStack.Clear();
        _redoStack.Clear();
        DiscardBatch();
        StateChanged?.Invoke();
    }
}
