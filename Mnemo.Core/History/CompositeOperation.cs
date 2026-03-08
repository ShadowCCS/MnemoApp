using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mnemo.Core.History;

/// <summary>
/// Groups multiple operations into a single undo/redo entry.
/// Apply replays in order; Rollback reverses in reverse order.
/// </summary>
public class CompositeOperation : IHistoryOperation
{
    private readonly List<IHistoryOperation> _operations;

    public string Description { get; }
    public OperationSource Source { get; }
    public IReadOnlyList<IHistoryOperation> Operations => _operations;

    public CompositeOperation(string description, OperationSource source, IEnumerable<IHistoryOperation> operations)
    {
        Description = description;
        Source = source;
        _operations = operations.ToList();
    }

    public async Task ApplyAsync()
    {
        foreach (var op in _operations)
            await op.ApplyAsync();
    }

    public async Task RollbackAsync()
    {
        for (int i = _operations.Count - 1; i >= 0; i--)
            await _operations[i].RollbackAsync();
    }
}
