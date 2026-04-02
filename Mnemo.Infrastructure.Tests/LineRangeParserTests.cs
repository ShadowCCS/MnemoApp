using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Tests;

public class LineRangeParserTests
{
    [Fact]
    public void TryParseRange_SingleNumber_StartEqualsEnd()
    {
        Assert.True(LineRangeParser.TryParseRange("5", out var a, out var b, out var err));
        Assert.Null(err);
        Assert.Equal(5, a);
        Assert.Equal(5, b);
    }

    [Fact]
    public void TryParseRange_Dash_Inclusive()
    {
        Assert.True(LineRangeParser.TryParseRange("1-3", out var a, out var b, out _));
        Assert.Equal(1, a);
        Assert.Equal(3, b);
    }

    [Fact]
    public void TryParseRange_EndBeforeStart_Fails()
    {
        Assert.False(LineRangeParser.TryParseRange("3-1", out _, out _, out var err));
        Assert.NotNull(err);
    }
}
