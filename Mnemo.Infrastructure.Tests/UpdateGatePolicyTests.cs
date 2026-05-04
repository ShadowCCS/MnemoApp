using Mnemo.Infrastructure.Services.Updates;

namespace Mnemo.Infrastructure.Tests;

public sealed class UpdateGatePolicyTests
{
    [Fact]
    public void IsAllowedRoute_accepts_overview_and_settings_only()
    {
        Assert.True(UpdateGatePolicy.IsAllowedRoute("overview"));
        Assert.True(UpdateGatePolicy.IsAllowedRoute("settings"));
        Assert.False(UpdateGatePolicy.IsAllowedRoute("notes"));
        Assert.False(UpdateGatePolicy.IsAllowedRoute(null));
    }

    [Fact]
    public void IsSnoozeActive_hybrid_ends_when_time_passes_or_launches_hit_zero()
    {
        var until = DateTime.UtcNow.AddHours(1);
        Assert.True(UpdateGatePolicy.IsSnoozeActive(until, 2));

        Assert.False(UpdateGatePolicy.IsSnoozeActive(DateTime.UtcNow.AddHours(-1), 2));

        Assert.False(UpdateGatePolicy.IsSnoozeActive(until, 0));
    }

    [Fact]
    public void IsSnoozeActive_time_only_when_launches_null()
    {
        var until = DateTime.UtcNow.AddHours(1);
        Assert.True(UpdateGatePolicy.IsSnoozeActive(until, null));
        Assert.False(UpdateGatePolicy.IsSnoozeActive(DateTime.UtcNow.AddHours(-1), null));
    }
}
