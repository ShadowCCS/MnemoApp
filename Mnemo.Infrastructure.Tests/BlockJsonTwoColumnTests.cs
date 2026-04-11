using System.Text.Json;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Tests;

public class BlockJsonTwoColumnTests
{
    [Fact]
    public void RoundTrip_TwoColumn_PreservesSplitRatioInPayload()
    {
        var options = new JsonSerializerOptions();
        var leftCell = new Block
        {
            Id = "a",
            Type = BlockType.Text,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain("L") }
        };
        var rightCell = new Block
        {
            Id = "b",
            Type = BlockType.Text,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain("R") }
        };
        var colL = new Block
        {
            Type = BlockType.ColumnGroup,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
            Children = new List<Block> { leftCell }
        };
        var colR = new Block
        {
            Type = BlockType.ColumnGroup,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
            Children = new List<Block> { rightCell }
        };
        var tc = new Block
        {
            Id = "split",
            Type = BlockType.TwoColumn,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
            Payload = new TwoColumnPayload(0.35),
            Children = new List<Block> { colL, colR }
        };

        var json = JsonSerializer.Serialize(tc, options);
        var back = JsonSerializer.Deserialize<Block>(json, options);

        Assert.NotNull(back);
        Assert.Equal(BlockType.TwoColumn, back.Type);
        var p = Assert.IsType<TwoColumnPayload>(back.Payload);
        Assert.Equal(0.35, p.SplitRatio, precision: 5);
    }
}
