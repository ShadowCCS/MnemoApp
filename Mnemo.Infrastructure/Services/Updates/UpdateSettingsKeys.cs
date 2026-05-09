namespace Mnemo.Infrastructure.Services.Updates;

public static class UpdateSettingsKeys
{
    public const string AutoCheck = "Updates.AutoCheck";
    public const string RemindAtUtc = "Updates.RemindAtUtc";
    public const string SnoozeLaunchesRemaining = "Updates.SnoozeLaunchesRemaining";
    public const string SkippedVersion = "Updates.SkippedVersion";
    public const string LastCheckedUtc = "Updates.LastCheckedUtc";
    public const string PromptCountByVersion = "Updates.PromptCountByVersion";

    /// <summary>Set immediately before in-app restart so the next launch can show a one-shot success toast.</summary>
    public const string PendingPostUpdateToastVersion = "Updates.PendingPostUpdateToastVersion";
}
