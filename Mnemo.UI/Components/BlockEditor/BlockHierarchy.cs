using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>Flat / tree helpers for document blocks (top-level list + nested <see cref="TwoColumnBlockViewModel"/> columns).</summary>
internal static class BlockHierarchy
{
    /// <summary>Depth-first document order: each top-level row, then left column top-to-bottom then right column.</summary>
    public static IEnumerable<BlockViewModel> EnumerateInDocumentOrder(IReadOnlyList<BlockViewModel> topLevel)
    {
        foreach (var b in topLevel)
        {
            if (b is TwoColumnBlockViewModel tc)
            {
                foreach (var x in tc.LeftColumnBlocks) yield return x;
                foreach (var x in tc.RightColumnBlocks) yield return x;
            }
            else
                yield return b;
        }
    }

    public static BlockViewModel? FindById(IReadOnlyList<BlockViewModel> topLevel, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var b in EnumerateInDocumentOrder(topLevel))
        {
            if (b.Id == id) return b;
        }
        return null;
    }

    public static BlockViewModel? FindFocused(IReadOnlyList<BlockViewModel> topLevel)
    {
        foreach (var b in EnumerateInDocumentOrder(topLevel))
        {
            if (b.IsFocused) return b;
        }
        return null;
    }

    public static BlockViewModel? FindPreviousInDocumentOrder(IReadOnlyList<BlockViewModel> topLevel, BlockViewModel vm)
    {
        var list = EnumerateInDocumentOrder(topLevel).ToList();
        var i = list.FindIndex(b => ReferenceEquals(b, vm));
        if (i <= 0) return null;
        return list[i - 1];
    }

    public static BlockViewModel? FindNextInDocumentOrder(IReadOnlyList<BlockViewModel> topLevel, BlockViewModel vm)
    {
        var list = EnumerateInDocumentOrder(topLevel).ToList();
        var i = list.FindIndex(b => ReferenceEquals(b, vm));
        if (i < 0 || i >= list.Count - 1) return null;
        return list[i + 1];
    }

    /// <summary>Top-level index of the row that contains <paramref name="vm"/> (split row counts as one).</summary>
    public static int GetTopLevelIndex(IReadOnlyList<BlockViewModel> topLevel, BlockViewModel vm)
    {
        if (vm.OwnerTwoColumn is { } tc)
        {
            for (int i = 0; i < topLevel.Count; i++)
            {
                if (ReferenceEquals(topLevel[i], tc)) return i;
            }
            return -1;
        }
        for (int i = 0; i < topLevel.Count; i++)
        {
            if (ReferenceEquals(topLevel[i], vm)) return i;
        }
        return -1;
    }

    public static void WireChildOwnership(TwoColumnBlockViewModel parent, BlockViewModel child, bool leftColumn)
    {
        child.OwnerTwoColumn = parent;
        child.IsLeftColumn = leftColumn;
    }

    public static void ClearChildOwnership(BlockViewModel child)
    {
        child.OwnerTwoColumn = null;
        child.IsLeftColumn = false;
    }

    /// <summary>Remove ownership from all children (used when tearing down a split).</summary>
    public static void ClearColumnOwnership(ObservableCollection<BlockViewModel> column)
    {
        foreach (var c in column)
            ClearChildOwnership(c);
    }

    public static Block ColumnGroupBlockFromVms(IEnumerable<BlockViewModel> blocks)
    {
        var list = new List<Block>();
        foreach (var vm in blocks)
            list.Add(vm.ToBlock());
        return new Block
        {
            Id = System.Guid.NewGuid().ToString(),
            Type = BlockType.ColumnGroup,
            InlineRuns = new List<InlineRun> { InlineRun.Plain(string.Empty) },
            Children = list
        };
    }
}
