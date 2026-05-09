using System;
using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IToastService
{
    /// <summary>
    /// Queues a toast. <paramref name="duration"/> of <see cref="TimeSpan.Zero"/> keeps the toast until dismissed.
    /// </summary>
    void SpawnToast(ToastType toastType, TimeSpan duration, string title, string description, ToastActionSpec? actions = null);

    /// <summary>Convenience: duration in seconds (0 = sticky).</summary>
    void SpawnToast(ToastType toastType, double durationSeconds, string title, string description, ToastActionSpec? actions = null) =>
        SpawnToast(toastType, TimeSpan.FromSeconds(durationSeconds), title, description, actions);

    /// <summary>Newest-first slice of notification history for the flyout.</summary>
    IReadOnlyList<NotificationHistoryEntry> GetRecentNotifications(int maxCount);

    event EventHandler? NotificationHistoryChanged;
}
