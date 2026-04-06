namespace Mnemo.UI.Components.BlockEditor;

/// <summary>One visual row in the block list (single block or a side-by-side pair).</summary>
public abstract class BlockRowViewModelBase
{
    /// <summary>Index of the first <see cref="BlockViewModel"/> in <see cref="BlockEditor.Blocks"/> for this row.</summary>
    public int StartBlockIndex { get; protected init; }

    /// <summary>1 or 2 — how many consecutive document blocks this row consumes.</summary>
    public int BlockSpan { get; protected init; }
}

public sealed class SingleBlockRowViewModel : BlockRowViewModelBase
{
    public BlockViewModel Block { get; }

    public SingleBlockRowViewModel(BlockViewModel block, int startBlockIndex)
    {
        Block = block;
        StartBlockIndex = startBlockIndex;
        BlockSpan = 1;
    }
}

public sealed class SplitBlockRowViewModel : BlockRowViewModelBase
{
    public BlockViewModel Left { get; }
    public BlockViewModel Right { get; }

    public SplitBlockRowViewModel(BlockViewModel left, BlockViewModel right, int startBlockIndex)
    {
        Left = left;
        Right = right;
        StartBlockIndex = startBlockIndex;
        BlockSpan = 2;
    }
}
