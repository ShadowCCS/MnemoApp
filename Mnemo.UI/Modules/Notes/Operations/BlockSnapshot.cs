using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.Operations;

/// <summary>
/// Deep-copy snapshot of a Block for undo state. Stores inline spans
/// so formatting is preserved across undo/redo.
/// </summary>
public sealed class BlockSnapshot
{
    public string Id { get; }
    public BlockType Type { get; }
    public List<InlineSpan> Spans { get; }
    public BlockPayload Payload { get; }
    public Dictionary<string, object> Meta { get; }
    public int Order { get; }
    /// <summary>Nested blocks (e.g. <see cref="BlockType.TwoColumn"/>).</summary>
    public BlockSnapshot[]? Children { get; }

    public BlockSnapshot(
        string id,
        BlockType type,
        List<InlineSpan> spans,
        BlockPayload payload,
        Dictionary<string, object> meta,
        int order,
        BlockSnapshot[]? children = null)
    {
        Id = id;
        Type = type;
        Spans = new List<InlineSpan>(spans);
        Payload = payload;
        Meta = new Dictionary<string, object>(meta);
        Order = order;
        Children = children;
    }

    public static BlockSnapshot From(Block block)
    {
        block.EnsureSpans();
        BlockSnapshot[]? children = null;
        if (block.Children is { Count: > 0 })
            children = block.Children.Select(From).ToArray();

        return new(block.Id, block.Type,
            new List<InlineSpan>(block.Spans),
            block.Payload,
            block.Meta ?? new Dictionary<string, object>(), block.Order, children);
    }

    public Block ToBlock()
    {
        var b = new Block
        {
            Id = Id,
            Type = Type,
            Spans = new List<InlineSpan>(Spans),
            Payload = Payload,
            Meta = new Dictionary<string, object>(Meta),
            Order = Order
        };
        if (Children is { Length: > 0 })
            b.Children = Children.Select(c => c.ToBlock()).ToList();
        return b;
    }

    public static BlockSnapshot[] SnapshotAll(IEnumerable<Block> blocks) =>
        blocks.Select(From).ToArray();
}

/// <summary>
/// Captures caret/selection state so undo feels correct after merges/splits.
/// Uses <see cref="BlockId"/> so nested blocks inside a split are addressable.
/// </summary>
public sealed class CaretState
{
    public string BlockId { get; init; } = "";
    public int CaretPosition { get; init; }
}
