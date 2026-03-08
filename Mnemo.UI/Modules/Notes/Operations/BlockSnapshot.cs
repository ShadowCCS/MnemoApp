using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Deep-copy snapshot of a Block for undo state. Ensures undo does not
/// share mutable dictionaries or lists with the live document.
/// </summary>
public sealed class BlockSnapshot
{
    public string Id { get; }
    public BlockType Type { get; }
    public string Content { get; }
    public Dictionary<string, object> Meta { get; }
    public int Order { get; }

    public BlockSnapshot(string id, BlockType type, string content, Dictionary<string, object> meta, int order)
    {
        Id = id;
        Type = type;
        Content = content;
        Meta = new Dictionary<string, object>(meta);
        Order = order;
    }

    public static BlockSnapshot From(Block block) =>
        new(block.Id, block.Type, block.Content ?? string.Empty,
            block.Meta ?? new Dictionary<string, object>(), block.Order);

    public Block ToBlock() => new()
    {
        Id = Id,
        Type = Type,
        Content = Content,
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
