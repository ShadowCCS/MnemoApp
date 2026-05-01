using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Statistics;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Statistics;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Tests.Statistics;

public sealed class StatisticsToolServiceTests
{
    [Fact]
    public async Task CreateRecordAsync_then_exists_and_read_fields()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var svc = new StatisticsToolService(mgr);

        var create = await svc.CreateRecordAsync(new StatsWriteRecordParameters
        {
            Namespace = "ext.demo",
            Kind = "widget.counter",
            Key = "one",
            Fields = new Dictionary<string, object?> { ["hits"] = 1L }
        });
        Assert.True(create.Ok);

        var exists = await svc.ExistsAsync(new StatsRecordIdentityParameters
        {
            Namespace = "ext.demo",
            Kind = "widget.counter",
            Key = "one"
        });
        Assert.True(exists.Ok);
        Assert.Equal(ToolResultCodes.Success, exists.Code);

        var fields = await svc.ReadFieldsAsync(new StatsReadFieldsParameters
        {
            Namespace = "ext.demo",
            Kind = "widget.counter",
            Key = "one",
            Fields = new List<string> { "hits" }
        });
        Assert.True(fields.Ok);
        Assert.NotNull(fields.Data);
    }

    [Fact]
    public async Task UpdateRecordAsync_merge_mode_preserves_other_fields()
    {
        IStatisticsManager mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var svc = new StatisticsToolService(mgr);

        await svc.CreateRecordAsync(new StatsWriteRecordParameters
        {
            Namespace = "ext.demo",
            Kind = "doc",
            Key = "a",
            Fields = new Dictionary<string, object?>
            {
                ["x"] = 1L,
                ["y"] = "keep"
            }
        });

        var upd = await svc.UpdateRecordAsync(new StatsWriteRecordParameters
        {
            Namespace = "ext.demo",
            Kind = "doc",
            Key = "a",
            MergeMode = "merge",
            Fields = new Dictionary<string, object?> { ["x"] = 2L }
        });
        Assert.True(upd.Ok);

        var stored = await mgr.GetAsync("ext.demo", "doc", "a");
        Assert.True(stored.IsSuccess);
        Assert.NotNull(stored.Value);
        Assert.Equal(2L, stored.Value.Fields["x"].AsInt());
        Assert.Equal("keep", stored.Value.Fields["y"].AsString());
    }
}
