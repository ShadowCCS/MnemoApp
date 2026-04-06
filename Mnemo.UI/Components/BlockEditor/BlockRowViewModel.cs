namespace Mnemo.UI.Components.BlockEditor;

/// <summary>One visual row in the block list (single block or a nested split row).</summary>
public abstract class BlockRowViewModelBase
{
    /// <summary>Index of the first <see cref="BlockViewModel"/> in <see cref="BlockEditor.Blocks"/> for this row.</summary>
    public int StartBlockIndex { get; protected init; }

    /// <summary>How many consecutive top-level <see cref="BlockEditor.Blocks"/> entries this row consumes (always 1 for a split; columns are nested).</summary>
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
    public TwoColumnBlockViewModel TwoColumn { get; }

    public SplitBlockRowViewModel(TwoColumnBlockViewModel twoColumn, int startBlockIndex)
    {
        TwoColumn = twoColumn;
        StartBlockIndex = startBlockIndex;
        BlockSpan = 1;
    }
}
