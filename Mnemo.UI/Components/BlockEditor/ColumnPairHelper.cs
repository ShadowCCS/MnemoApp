using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>Side-by-side layout: two top-level blocks share <c>columnPairId</c> + <c>columnSide</c> meta.</summary>
public static class ColumnPairHelper
{
    public const string PairIdKey = "columnPairId";
    public const string SideKey = "columnSide";
    public const string Left = "Left";
    public const string Right = "Right";

    public static string? GetPairId(BlockViewModel vm) => MetaString(vm, PairIdKey);

    /// <summary><see cref="Left"/> or <see cref="Right"/> when paired.</summary>
    public static string? GetColumnSide(BlockViewModel vm) => MetaString(vm, SideKey);

    private static string? MetaString(BlockViewModel vm, string key)
    {
        if (!vm.Meta.TryGetValue(key, out var v) || v == null) return null;
        return v.ToString();
    }

    public static bool IsPairedLeft(BlockViewModel vm, int index, IReadOnlyList<BlockViewModel> blocks) =>
        GetColumnSide(vm) == Left
        && index + 1 < blocks.Count
        && GetPairId(vm) == GetPairId(blocks[index + 1])
        && GetColumnSide(blocks[index + 1]) == Right;

    public static bool IsPairedRight(BlockViewModel vm, int index, IReadOnlyList<BlockViewModel> blocks) =>
        GetColumnSide(vm) == Right
        && index > 0
        && GetPairId(vm) == GetPairId(blocks[index - 1])
        && GetColumnSide(blocks[index - 1]) == Left;

    public static BlockViewModel? GetSibling(BlockViewModel vm, IReadOnlyList<BlockViewModel> document)
    {
        var pairId = GetPairId(vm);
        if (string.IsNullOrEmpty(pairId)) return null;
        var idx = -1;
        for (int i = 0; i < document.Count; i++)
        {
            if (ReferenceEquals(document[i], vm))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0) return null;
        if (GetColumnSide(vm) == Left && idx + 1 < document.Count
            && GetPairId(document[idx + 1]) == pairId && GetColumnSide(document[idx + 1]) == Right)
            return document[idx + 1];
        if (GetColumnSide(vm) == Right && idx > 0
            && GetPairId(document[idx - 1]) == pairId && GetColumnSide(document[idx - 1]) == Left)
            return document[idx - 1];
        return null;
    }

    public static void WirePair(BlockViewModel left, BlockViewModel right, string pairId, double splitRatio = 0.5)
    {
        left.Meta[PairIdKey] = pairId;
        left.Meta[SideKey] = Left;
        left.Meta["columnSplitRatio"] = Math.Clamp(splitRatio, 0.1, 0.9);
        right.Meta[PairIdKey] = pairId;
        right.Meta[SideKey] = Right;
        right.Meta.Remove("columnSplitRatio");
    }

    public static void ClearPair(BlockViewModel? a, BlockViewModel? b)
    {
        if (a != null)
        {
            a.Meta.Remove(PairIdKey);
            a.Meta.Remove(SideKey);
            a.Meta.Remove("columnSplitRatio");
        }
        if (b != null)
        {
            b.Meta.Remove(PairIdKey);
            b.Meta.Remove(SideKey);
            b.Meta.Remove("columnSplitRatio");
        }
    }

    /// <summary>Legacy <see cref="BlockType.TwoColumn"/> + <see cref="Block.Children"/> → two flat blocks with pair meta.</summary>
    public static List<Block> ExpandLegacyTwoColumnBlocks(IEnumerable<Block> blocks)
    {
        var list = new List<Block>();
        foreach (var b in blocks.Where(x => x != null).OrderBy(x => x.Order))
        {
            if (b.Type == BlockType.TwoColumn && b.Children is { Count: >= 2 })
            {
                var id = Guid.NewGuid().ToString();
                var left = CloneBlockForPairChild(b.Children[0]);
                var right = CloneBlockForPairChild(b.Children[1]);
                left.Meta ??= new Dictionary<string, object>();
                right.Meta ??= new Dictionary<string, object>();
                left.Meta[PairIdKey] = id;
                left.Meta[SideKey] = Left;
                right.Meta[PairIdKey] = id;
                right.Meta[SideKey] = Right;
                if (b.Meta != null && b.Meta.TryGetValue("columnSplitRatio", out var ratio))
                {
                    left.Meta["columnSplitRatio"] = ratio is double d ? d : 0.5;
                }
                else
                    left.Meta["columnSplitRatio"] = 0.5;
                left.Order = b.Order;
                right.Order = b.Order;
                list.Add(left);
                list.Add(right);
            }
            else
                list.Add(b);
        }
        return list;
    }

    private static Block CloneBlockForPairChild(Block source)
    {
        source.EnsureInlineRuns();
        return new Block
        {
            Id = string.IsNullOrEmpty(source.Id) ? Guid.NewGuid().ToString() : source.Id,
            Type = source.Type,
            InlineRuns = new List<InlineRun>(source.InlineRuns!),
            Meta = new Dictionary<string, object>(source.Meta ?? new Dictionary<string, object>()),
            Order = source.Order
        };
    }
}
