using Mnemo.Core.Models;

namespace Mnemo.UI.Components.BlockEditor;

public static class BlockFactory
{
    public static BlockViewModel CreateBlock(BlockType type, int order = 0)
    {
        return type switch
        {
            BlockType.Text => new BlockViewModel(type, "", order),
            BlockType.Heading1 => new BlockViewModel(type, "", order),
            BlockType.Heading2 => new BlockViewModel(type, "", order),
            BlockType.Heading3 => new BlockViewModel(type, "", order),
            BlockType.Heading4 => new BlockViewModel(type, "", order),
            BlockType.Code => CreateCodeBlock(order),
            BlockType.BulletList => new BlockViewModel(type, "", order),
            BlockType.NumberedList => new BlockViewModel(type, "", order),
            BlockType.Checklist => CreateChecklistBlock(order),
            BlockType.Quote => new BlockViewModel(type, "", order),
            BlockType.Divider => new BlockViewModel(type, "", order),
            BlockType.Image => CreateImageBlock(order),
            BlockType.TwoColumn => CreateTwoColumnBlock(order),
            BlockType.Equation => CreateEquationBlock(order),
            BlockType.Page => CreatePageBlock(order),
            _ => new BlockViewModel(BlockType.Text, "", order)
        };
    }

    private static BlockViewModel CreateTwoColumnBlock(int order)
    {
        var tc = new TwoColumnBlockViewModel(order);
        var left = new BlockViewModel(BlockType.Text, "", 0);
        var right = new BlockViewModel(BlockType.Text, "", 0);
        BlockHierarchy.WireChildOwnership(tc, left, true);
        BlockHierarchy.WireChildOwnership(tc, right, false);
        tc.LeftColumnBlocks.Add(left);
        tc.RightColumnBlocks.Add(right);
        return tc;
    }

    private static BlockViewModel CreateCodeBlock(int order)
    {
        var block = new BlockViewModel(BlockType.Code, "", order);
        block.CodeLanguage = "csharp";
        return block;
    }

    private static BlockViewModel CreateChecklistBlock(int order)
    {
        return new BlockViewModel(BlockType.Checklist, "", order);
    }

    private static BlockViewModel CreateImageBlock(int order)
    {
        var block = new BlockViewModel(BlockType.Image, "", order);
        return block;
    }

    private static BlockViewModel CreateEquationBlock(int order)
    {
        var block = new BlockViewModel(BlockType.Equation, "", order);
        block.EquationLatex = string.Empty;
        return block;
    }

    private static BlockViewModel CreatePageBlock(int order)
    {
        var block = new BlockViewModel(BlockType.Page, "", order);
        block.ReferenceNoteId = string.Empty;
        return block;
    }
}


