using System.Threading.Tasks;

namespace Mnemo.Core.History;

/// <summary>
/// Identifies the module that originated an operation, for traceability.
/// </summary>
public enum OperationSource
{
    NotesEditor,
    MindmapEditor,
    AIAction
}

/// <summary>
/// A reversible unit of work. Implementations capture enough state to undo/redo
/// one logical user intent (which may be a composite of low-level mutations).
/// </summary>
public interface IHistoryOperation
{
    string Description { get; }
    OperationSource Source { get; }
    Task ApplyAsync();
    Task RollbackAsync();
}
