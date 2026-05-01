using Mnemo.Core.Models;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Statistics;

namespace Mnemo.Infrastructure.Tests.Statistics;

public sealed class StatisticsManagerTests
{
    [Fact]
    public async Task CreateAsync_duplicate_identity_fails()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var write = new StatisticsRecordWrite
        {
            Namespace = "test",
            Kind = "k",
            Key = "a",
            SourceModule = "test",
            Fields = new Dictionary<string, StatValue>(StringComparer.Ordinal)
            {
                ["n"] = StatValue.FromInt(1)
            }
        };

        var first = await mgr.CreateAsync(write);
        Assert.True(first.IsSuccess);

        var second = await mgr.CreateAsync(write);
        Assert.False(second.IsSuccess);
        Assert.Contains("already exists", second.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetFieldsAsync_returns_only_requested_keys()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        await mgr.CreateAsync(new StatisticsRecordWrite
        {
            Namespace = "test",
            Kind = "k",
            Key = "row",
            SourceModule = "test",
            Fields = new Dictionary<string, StatValue>(StringComparer.Ordinal)
            {
                ["a"] = StatValue.FromInt(1),
                ["b"] = StatValue.FromString("x")
            }
        });

        var r = await mgr.GetFieldsAsync("test", "k", "row", new[] { "b", "missing" });
        Assert.True(r.IsSuccess);
        Assert.NotNull(r.Value);
        Assert.Single(r.Value);
        Assert.Equal("x", r.Value["b"].AsString());
    }

    [Fact]
    public async Task UpdateAsync_wrong_expected_version_fails()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var created = await mgr.CreateAsync(new StatisticsRecordWrite
        {
            Namespace = "test",
            Kind = "k",
            Key = "v",
            SourceModule = "test",
            Fields = new Dictionary<string, StatValue>(StringComparer.Ordinal) { ["x"] = StatValue.FromInt(1) }
        });
        Assert.True(created.IsSuccess);
        Assert.Equal(1L, created.Value!.Version);

        var bad = await mgr.UpdateAsync(
            new StatisticsRecordWrite
            {
                Namespace = "test",
                Kind = "k",
                Key = "v",
                SourceModule = "test",
                Fields = new Dictionary<string, StatValue>(StringComparer.Ordinal) { ["x"] = StatValue.FromInt(2) }
            },
            StatisticsFieldMergeMode.Merge,
            expectedVersion: 99);
        Assert.False(bad.IsSuccess);
        Assert.Contains("conflict", bad.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncrementAsync_creates_then_adds_delta()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var r1 = await mgr.IncrementAsync("test", "k", "c", "count", 5, "test");
        Assert.True(r1.IsSuccess);
        Assert.Equal(5L, r1.Value);

        var r2 = await mgr.IncrementAsync("test", "k", "c", "count", 3, "test");
        Assert.True(r2.IsSuccess);
        Assert.Equal(8L, r2.Value);
    }

    [Fact]
    public async Task ExistsAsync_round_trips()
    {
        var mgr = StatisticsManager.CreateInMemory(new TestLogger());
        var before = await mgr.ExistsAsync("test", "k", "e");
        Assert.True(before.IsSuccess);
        Assert.False(before.Value);

        await mgr.CreateAsync(new StatisticsRecordWrite
        {
            Namespace = "test",
            Kind = "k",
            Key = "e",
            SourceModule = "test",
            Fields = new Dictionary<string, StatValue>(StringComparer.Ordinal)
        });

        var after = await mgr.ExistsAsync("test", "k", "e");
        Assert.True(after.IsSuccess);
        Assert.True(after.Value);
    }
}
