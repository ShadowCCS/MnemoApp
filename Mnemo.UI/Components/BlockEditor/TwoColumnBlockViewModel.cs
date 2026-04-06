using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>One document row: two columns, each an ordered stack of blocks.</summary>
public sealed class TwoColumnBlockViewModel : BlockViewModel
{
    public ObservableCollection<BlockViewModel> LeftColumnBlocks { get; } = new();
    public ObservableCollection<BlockViewModel> RightColumnBlocks { get; } = new();

    /// <summary>Column collection mutated (insert/remove/replace). Used by <see cref="BlockEditor"/> to subscribe children.</summary>
    public event Action<TwoColumnBlockViewModel, bool, NotifyCollectionChangedEventArgs>? ColumnChildrenChanged;

    public TwoColumnBlockViewModel(int order = 0) : base(BlockType.TwoColumn, "", order)
    {
        Type = BlockType.TwoColumn;
        // Empty placeholder for rich-text; not used for layout.
        SetRuns(new List<InlineRun> { InlineRun.Plain(string.Empty) });
        if (!Meta.ContainsKey("columnSplitRatio"))
            Meta["columnSplitRatio"] = 0.5;
        LeftColumnBlocks.CollectionChanged += (_, e) => ColumnChildrenChanged?.Invoke(this, true, e);
        RightColumnBlocks.CollectionChanged += (_, e) => ColumnChildrenChanged?.Invoke(this, false, e);
    }

    /// <summary>Deserialize nested <see cref="BlockType.TwoColumn"/> + <see cref="BlockType.ColumnGroup"/> children.</summary>
    public TwoColumnBlockViewModel(Block block) : base(block)
    {
        Type = BlockType.TwoColumn;
        LeftColumnBlocks.CollectionChanged += (_, e) => ColumnChildrenChanged?.Invoke(this, true, e);
        RightColumnBlocks.CollectionChanged += (_, e) => ColumnChildrenChanged?.Invoke(this, false, e);
        LeftColumnBlocks.Clear();
        RightColumnBlocks.Clear();

        if (block.Children is not { Count: >= 2 })
            return;

        var left = block.Children[0];
        var right = block.Children[1];

        void LoadFromChild(Block columnBlock, ObservableCollection<BlockViewModel> target, bool leftColumn)
        {
            if (columnBlock.Type == BlockType.ColumnGroup && columnBlock.Children is { Count: > 0 })
            {
                foreach (var child in columnBlock.Children.OrderBy(c => c.Order))
                {
                    var vm = new BlockViewModel(child);
                    BlockHierarchy.WireChildOwnership(this, vm, leftColumn);
                    target.Add(vm);
                }
            }
            else
            {
                var vm = new BlockViewModel(columnBlock);
                BlockHierarchy.WireChildOwnership(this, vm, leftColumn);
                target.Add(vm);
            }
        }

        LoadFromChild(left, LeftColumnBlocks, true);
        LoadFromChild(right, RightColumnBlocks, false);
    }

    public static TwoColumnBlockViewModel FromFlatPair(BlockViewModel left, BlockViewModel right)
    {
        var ratio = left.ColumnSplitRatio;
        ColumnPairHelper.ClearPair(left, right);
        var tc = new TwoColumnBlockViewModel(left.Order)
        {
            Id = Guid.NewGuid().ToString()
        };
        tc.Meta["columnSplitRatio"] = ratio;
        BlockHierarchy.WireChildOwnership(tc, left, true);
        BlockHierarchy.WireChildOwnership(tc, right, false);
        tc.LeftColumnBlocks.Add(left);
        tc.RightColumnBlocks.Add(right);
        return tc;
    }

    public override Block ToBlock()
    {
        var leftGroup = BlockHierarchy.ColumnGroupBlockFromVms(LeftColumnBlocks);
        var rightGroup = BlockHierarchy.ColumnGroupBlockFromVms(RightColumnBlocks);
        return new Block
        {
            Id = Id,
            Type = BlockType.TwoColumn,
            InlineRuns = new List<InlineRun> { InlineRun.Plain(string.Empty) },
            Meta = Meta,
            Order = Order,
            Children = new List<Block> { leftGroup, rightGroup }
        };
    }
}
