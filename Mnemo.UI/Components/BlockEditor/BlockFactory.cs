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
            BlockType.Code => CreateCodeBlock(order),
            BlockType.BulletList => new BlockViewModel(type, "", order),
            BlockType.NumberedList => new BlockViewModel(type, "", order),
            BlockType.Checklist => CreateChecklistBlock(order),
            BlockType.Quote => new BlockViewModel(type, "", order),
            BlockType.Divider => new BlockViewModel(type, "", order),
            _ => new BlockViewModel(BlockType.Text, "", order)
        };
    }

    private static BlockViewModel CreateCodeBlock(int order)
    {
        var block = new BlockViewModel(BlockType.Code, "", order);
        block.Meta["language"] = "csharp";
        return block;
    }

    private static BlockViewModel CreateChecklistBlock(int order)
    {
        return new BlockViewModel(BlockType.Checklist, "", order);
    }
}


