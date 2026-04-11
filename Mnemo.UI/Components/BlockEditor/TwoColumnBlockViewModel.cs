using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>One document row: two columns, each an ordered stack of blocks. Split ratio lives in <see cref="TwoColumnPayload"/>, not per-cell meta.</summary>
public sealed class TwoColumnBlockViewModel : BlockViewModel
{
    private double _columnSplitRatio = 0.5;

    public ObservableCollection<BlockViewModel> LeftColumnBlocks { get; } = new();
    public ObservableCollection<BlockViewModel> RightColumnBlocks { get; } = new();

    /// <summary>Column collection mutated (insert/remove/replace). Used by <see cref="BlockEditor"/> to subscribe children.</summary>
    public event Action<TwoColumnBlockViewModel, bool, NotifyCollectionChangedEventArgs>? ColumnChildrenChanged;

    public TwoColumnBlockViewModel(int order = 0) : base(BlockType.TwoColumn, "", order)
    {
        Type = BlockType.TwoColumn;
        SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });
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

        _columnSplitRatio = ReadSplitRatioFromBlock(block);
        if (Meta.Remove("columnSplitRatio"))
            OnPropertyChanged(nameof(Meta));

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

    public override double ColumnSplitRatio
    {
        get => _columnSplitRatio;
        set
        {
            var r = Math.Clamp(value, 0.1, 0.9);
            if (Math.Abs(_columnSplitRatio - r) < 1e-9)
                return;
            _columnSplitRatio = r;
            OnPropertyChanged();
        }
    }

    public static TwoColumnBlockViewModel FromFlatPair(BlockViewModel left, BlockViewModel right)
    {
        var ratio = left.ColumnSplitRatio;
        ColumnPairHelper.ClearPair(left, right);
        var tc = new TwoColumnBlockViewModel(left.Order)
        {
            Id = Guid.NewGuid().ToString(),
            _columnSplitRatio = ratio
        };
        tc.OnPropertyChanged(nameof(ColumnSplitRatio));
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
        var meta = new Dictionary<string, object>(Meta);
        meta.Remove("columnSplitRatio");
        meta.Remove(ColumnPairHelper.PairIdKey);
        meta.Remove(ColumnPairHelper.SideKey);
        return new Block
        {
            Id = Id,
            Type = BlockType.TwoColumn,
            Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
            Payload = new TwoColumnPayload(_columnSplitRatio),
            Meta = meta,
            Order = Order,
            Children = new List<Block> { leftGroup, rightGroup }
        };
    }

    private static double ReadSplitRatioFromBlock(Block block)
    {
        if (block.Payload is TwoColumnPayload tcp)
        {
            var r = tcp.SplitRatio;
            if (r <= 0 || r >= 1 || double.IsNaN(r))
                return 0.5;
            return Math.Clamp(r, 0.1, 0.9);
        }

        if (block.Meta != null && block.Meta.TryGetValue("columnSplitRatio", out var v) && v != null)
        {
            if (v is double d)
                return Math.Clamp(d, 0.1, 0.9);
            if (v is int i)
                return Math.Clamp(i, 0.1, 0.9);
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return Math.Clamp(je.GetDouble(), 0.1, 0.9);
        }

        return 0.5;
    }
}
