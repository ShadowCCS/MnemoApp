using Mnemo.Core.Models.Tools;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Tests;

public class ToolResultFormatterTests
{
    [Fact]
    public void Format_IncludesOkAndCode()
    {
        var f = new ToolResultFormatter();
        var json = f.Format(ToolInvocationResult.Success("done", new { x = 1 }));
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"code\":\"success\"", json);
        Assert.Contains("done", json);
    }
}
