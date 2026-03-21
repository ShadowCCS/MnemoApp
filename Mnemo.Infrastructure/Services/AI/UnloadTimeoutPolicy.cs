using System;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Parses <c>AI.UnloadTimeout</c> values. The UI persists <b>canonical keys</b> (<see cref="Never"/>, etc.);
/// legacy saves may still be English UI labels from an older dropdown.
/// </summary>
public static class UnloadTimeoutPolicy
{
    public const string Never = "Never";
    public const string FiveMinutes = "FiveMinutes";
    public const string FifteenMinutes = "FifteenMinutes";
    public const string OneHour = "OneHour";

    /// <summary>
    /// Maps a stored setting string to a canonical key. Returns <see langword="null"/> if the value cannot be recognized.
    /// </summary>
    public static string? TryNormalizeToCanonicalKey(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return FifteenMinutes;

        var s = stored.Trim();

        if (s.Equals(Never, StringComparison.OrdinalIgnoreCase))
            return Never;
        if (s.Equals(FiveMinutes, StringComparison.OrdinalIgnoreCase))
            return FiveMinutes;
        if (s.Equals(FifteenMinutes, StringComparison.OrdinalIgnoreCase))
            return FifteenMinutes;
        if (s.Equals(OneHour, StringComparison.OrdinalIgnoreCase))
            return OneHour;

        // Legacy: English labels only (old dropdown stored the visible English string)
        if (s.Equals("5 Minutes", StringComparison.OrdinalIgnoreCase))
            return FiveMinutes;
        if (s.Equals("15 Minutes", StringComparison.OrdinalIgnoreCase))
            return FifteenMinutes;
        if (s.Equals("1 Hour", StringComparison.OrdinalIgnoreCase))
            return OneHour;

        return null;
    }

    /// <summary>Returns <see langword="null"/> when the user chose never to unload.</summary>
    public static TimeSpan? ParseToIdleSpanOrNull(string? stored)
    {
        var key = TryNormalizeToCanonicalKey(stored);
        if (key == null)
            return TimeSpan.FromMinutes(15);

        return key switch
        {
            Never => null,
            FiveMinutes => TimeSpan.FromMinutes(5),
            FifteenMinutes => TimeSpan.FromMinutes(15),
            OneHour => TimeSpan.FromHours(1),
            _ => TimeSpan.FromMinutes(15)
        };
    }

    /// <summary>Mid/high tier models free VRAM sooner than low tier when using the same user setting.</summary>
    public static TimeSpan TierAdjustedIdle(TimeSpan baseIdle, bool isMidOrHigh)
    {
        if (!isMidOrHigh)
            return baseIdle;

        return TimeSpan.FromTicks(Math.Max(1, (long)(baseIdle.Ticks * 0.75)));
    }
}
