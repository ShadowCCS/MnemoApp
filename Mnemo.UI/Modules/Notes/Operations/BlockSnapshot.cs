using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Deep-copy snapshot of a Block for undo state. Stores inline runs
/// so formatting is preserved across undo/redo.
/// </summary>
public sealed class BlockSnapshot
{
    public string Id { get; }
    public BlockType Type { get; }
    public List<InlineRun> InlineRuns { get; }
    public Dictionary<string, object> Meta { get; }
    public int Order { get; }

    public BlockSnapshot(string id, BlockType type, List<InlineRun> inlineRuns, Dictionary<string, object> meta, int order)
    {
        Id = id;
        Type = type;
        InlineRuns = new List<InlineRun>(inlineRuns);
        Meta = new Dictionary<string, object>(meta);
        Order = order;
    }

    public static BlockSnapshot From(Block block)
    {
        block.EnsureInlineRuns();
        return new(block.Id, block.Type,
            new List<InlineRun>(block.InlineRuns!),
            block.Meta ?? new Dictionary<string, object>(), block.Order);
    }

    public Block ToBlock() => new()
    {
        Id = Id,
        Type = Type,
        InlineRuns = new List<InlineRun>(InlineRuns),
        Meta = new Dictionary<string, object>(Meta),
        Order = Order
    };

    public static BlockSnapshot[] SnapshotAll(IEnumerable<Block> blocks) =>
        blocks.Select(From).ToArray();
}

/// <summary>
/// Captures caret/selection state so undo feels correct after merges/splits.
/// </summary>
public sealed class CaretState
{
    public int BlockIndex { get; init; }
    public int CaretPosition { get; init; }
}
