using Mnemo.Core.Formatting;
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
            new()
            {
                Type = BlockType.Heading1,
                Order = 0,
                Spans = new List<InlineSpan> { new TextSpan("Title", new TextStyle(Bold: true)) }
            },
            new() { Type = BlockType.BulletList, Order = 1, Spans = new List<InlineSpan> { InlineSpan.Plain("Item") } },
            new() { Type = BlockType.Text, Order = 2, Spans = new List<InlineSpan> { InlineSpan.Plain("Para") } }
        };
        foreach (var b in blocks) b.EnsureSpans();

        var md = NoteBlockMarkdownConverter.Serialize(blocks);
        var back = NoteBlockMarkdownConverter.Deserialize(md);
        Assert.True(back.Count >= 3);
        Assert.Contains(back, b => b.Type == BlockType.Heading1);
        Assert.Contains(back, b => b.Type == BlockType.BulletList);
    }

    [Fact]
    public void Serialize_EquationBlock_EmitsDoubleDollar()
    {
        var block = new Block
        {
            Type = BlockType.Equation,
            Order = 0,
            Payload = new EquationPayload(@"\frac{1}{2}")
        };
        block.EnsureSpans();

        var md = NoteBlockMarkdownConverter.Serialize(new List<Block> { block });
        Assert.Contains("$$", md);
        Assert.Contains(@"\frac{1}{2}", md);
    }

    [Fact]
    public void RoundTrip_EquationBlock_PreservesLatex()
    {
        var blocks = new List<Block>
        {
            new()
            {
                Type = BlockType.Equation,
                Order = 0,
                Payload = new EquationPayload(@"x^2 + y^2 = z^2")
            }
        };
        foreach (var b in blocks) b.EnsureSpans();

        var md = NoteBlockMarkdownConverter.Serialize(blocks);
        var back = NoteBlockMarkdownConverter.Deserialize(md);

        Assert.Single(back);
        Assert.Equal(BlockType.Equation, back[0].Type);
        Assert.Equal("x^2 + y^2 = z^2", (back[0].Payload as EquationPayload)?.Latex);
    }

    [Fact]
    public void Deserialize_SingleLineEquation_Parses()
    {
        var md = "$$E=mc^2$$";
        var blocks = NoteBlockMarkdownConverter.Deserialize(md);

        Assert.Single(blocks);
        Assert.Equal(BlockType.Equation, blocks[0].Type);
        Assert.Equal("E=mc^2", (blocks[0].Payload as EquationPayload)?.Latex);
    }

    [Fact]
    public void InlineMarkdownSerializer_EquationSpan_EmitsDollar()
    {
        var spans = new List<InlineSpan>
        {
            InlineSpan.Plain("Energy is "),
            new EquationSpan("E=mc^2"),
            InlineSpan.Plain(" (Einstein)")
        };

        var md = InlineMarkdownSerializer.SerializeSpans(spans);
        Assert.Equal("Energy is $E=mc^2$ (Einstein)", md);
    }

    [Fact]
    public void RoundTrip_Checklist_PreservesChecked()
    {
        var blocks = new List<Block>
        {
            new()
            {
                Type = BlockType.Checklist,
                Order = 0,
                Payload = new ChecklistPayload(true),
                Spans = new List<InlineSpan> { InlineSpan.Plain("Done") }
            },
            new()
            {
                Type = BlockType.Checklist,
                Order = 1,
                Payload = new ChecklistPayload(false),
                Spans = new List<InlineSpan> { InlineSpan.Plain("Todo") }
            }
        };
        foreach (var b in blocks) b.EnsureSpans();

        var md = NoteBlockMarkdownConverter.Serialize(blocks);
        var back = NoteBlockMarkdownConverter.Deserialize(md);

        Assert.Equal(2, back.Count);
        Assert.Equal(BlockType.Checklist, back[0].Type);
        Assert.Equal(BlockType.Checklist, back[1].Type);
        Assert.True((back[0].Payload as ChecklistPayload)?.Checked);
        Assert.False((back[1].Payload as ChecklistPayload)?.Checked);
        Assert.Equal("Done", back[0].Content);
        Assert.Equal("Todo", back[1].Content);
    }

    [Fact]
    public void EquationLatexNormalizer_StripsDollarDelimiters()
    {
        Assert.Equal("x^2", EquationLatexNormalizer.Normalize("$x^2$"));
        Assert.Equal("x^2", EquationLatexNormalizer.Normalize("$$x^2$$"));
        Assert.Equal(@"\frac{1}{2}", EquationLatexNormalizer.Normalize(@"\frac{1}{2}"));
        Assert.Equal(string.Empty, EquationLatexNormalizer.Normalize(""));
        Assert.Equal(string.Empty, EquationLatexNormalizer.Normalize(null));
    }
}
