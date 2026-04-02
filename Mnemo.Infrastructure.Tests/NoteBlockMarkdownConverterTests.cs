using Mnemo.Core.Models;
using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.Infrastructure.Tests;

public class NoteBlockMarkdownConverterTests
{
    [Fact]
    public void RoundTrip_PreservesMultipleBlockTypes()
    {
        var blocks = new List<Block>
        {
            new() { Type = BlockType.Heading1, Order = 0, Content = "Title" },
            new() { Type = BlockType.BulletList, Order = 1, Content = "Item" },
            new() { Type = BlockType.Text, Order = 2, Content = "Para" }
        };
        foreach (var b in blocks) b.EnsureInlineRuns();

        var md = NoteBlockMarkdownConverter.Serialize(blocks);
        var back = NoteBlockMarkdownConverter.Deserialize(md);
        Assert.True(back.Count >= 3);
        Assert.Contains(back, b => b.Type == BlockType.Heading1);
        Assert.Contains(back, b => b.Type == BlockType.BulletList);
    }
}
