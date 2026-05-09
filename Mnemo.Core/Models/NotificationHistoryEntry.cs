using System;

namespace Mnemo.Core.Models;

/// <summary>One persisted notification for the history flyout (includes every spawned toast).</summary>
public sealed class NotificationHistoryEntry
{
    public Guid Id { get; init; }
    public ToastType ToastType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
