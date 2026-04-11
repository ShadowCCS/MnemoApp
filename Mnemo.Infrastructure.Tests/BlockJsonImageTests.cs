using System.Collections.Generic;
using System.Text.Json;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Tests;

public class BlockJsonImageTests
{
    [Fact]
    public void RoundTrip_Image_PayloadFieldsAndMetaKeysStripped()
    {
        var options = new JsonSerializerOptions();
        var block = new Block
        {
            Id = "img1",
            Type = BlockType.Image,
            Order = 0,
            Spans = new List<InlineSpan> { InlineSpan.Plain("caption") },
            Payload = new ImagePayload("/x/photo.png", "caption", 320, "center"),
            Meta = new Dictionary<string, object>
            {
                ["imagePath"] = "should-not-round-trip",
                ["imageAlt"] = "legacy",
                ["imageWidth"] = 1.0,
                ["imageAlign"] = "right",
                ["custom"] = "keep"
            }
        };

        var json = JsonSerializer.Serialize(block, options);
        Assert.DoesNotContain("imagePath", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<Block>(json, options);

        Assert.NotNull(back);
        Assert.Equal(BlockType.Image, back.Type);
        var p = Assert.IsType<ImagePayload>(back.Payload);
        Assert.Equal("/x/photo.png", p.Path);
        Assert.Equal(320, p.Width, precision: 5);
        Assert.Equal("center", p.Align);
        Assert.False(back.Meta.ContainsKey("imagePath"));
        Assert.True(back.Meta.TryGetValue("custom", out var c) && c?.ToString() == "keep");
    }

    [Fact]
    public void Deserialize_LegacyMetaOnly_BuildsImagePayload()
    {
        var options = new JsonSerializerOptions();
        var json = """
            {"id":"a","type":"Image","order":0,"spans":[{"kind":"text","text":"alt","style":{}}],"meta":{"imagePath":"/p.png","imageAlt":"alt","imageWidth":100,"imageAlign":"right"}}
            """;
        var back = JsonSerializer.Deserialize<Block>(json, options);
        Assert.NotNull(back);
        var p = Assert.IsType<ImagePayload>(back.Payload);
        Assert.Equal("/p.png", p.Path);
        Assert.Equal(100, p.Width, precision: 5);
        Assert.Equal("right", p.Align);
    }
}
