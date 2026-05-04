using System;

namespace Mnemo.Infrastructure.Services.Updates;

/// <summary>Pure helpers for when an update prompt may be shown.</summary>
public static class UpdateGatePolicy
{
    public static readonly string[] AllowedRoutes = { "overview", "settings" };

    public static bool IsAllowedRoute(string? route)
    {
        if (string.IsNullOrEmpty(route))
            return false;
        foreach (var allowed in AllowedRoutes)
        {
            if (string.Equals(route, allowed, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Snooze is active: user dismissed and we have not reached the resume condition yet.</summary>
    public static bool IsSnoozeActive(DateTime? snoozeUntilUtc, int? snoozeLaunchesRemaining)
    {
        if (!snoozeUntilUtc.HasValue)
            return false;

        if (snoozeLaunchesRemaining == null)
            return DateTime.UtcNow < snoozeUntilUtc.Value;

        return DateTime.UtcNow < snoozeUntilUtc.Value && snoozeLaunchesRemaining.Value > 0;
    }

    public static bool IsSkipped(string? skippedVersion, string updateVersion)
    {
        if (string.IsNullOrEmpty(skippedVersion))
            return false;
        return string.Equals(skippedVersion.Trim(), updateVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOverPromptCap(int promptCount, int maxPrompts = 3) => promptCount >= maxPrompts;
}
