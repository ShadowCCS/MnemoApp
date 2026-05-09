using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components;

public sealed class NotificationFlyoutRowViewModel
{
    public NotificationFlyoutRowViewModel(NotificationHistoryEntry entry)
    {
        ToastType = entry.ToastType;
        Title = entry.Title;
        Description = entry.Description;
        TimeText = entry.CreatedAt.ToLocalTime().ToString("HH:mm");
        TypeAccentBrush = ResolveTypeAccentBrush(ToastType);
    }

    public ToastType ToastType { get; }
    public string Title { get; }
    public string Description { get; }
    public string TimeText { get; }

    /// <summary>Theme accent for the notification dot (resolved once from <c>ToastAccent*Brush</c> resources).</summary>
    public IBrush TypeAccentBrush { get; }

    private static IBrush ResolveTypeAccentBrush(ToastType type)
    {
        var key = type switch
        {
            ToastType.Info => "ToastAccentInfoBrush",
            ToastType.Success => "ToastAccentSuccessBrush",
            ToastType.Warning => "ToastAccentWarningBrush",
            ToastType.Action => "ToastAccentActionBrush",
            ToastType.Task => "ToastAccentTaskBrush",
            _ => "ToastAccentInfoBrush",
        };

        var app = Application.Current;
        if (app != null
            && app.TryFindResource(key, app.ActualThemeVariant, out var res)
            && res is IBrush brush)
            return brush;

        return Brushes.Gray;
    }
}
