using System;
using System.Collections.ObjectModel;

namespace Mnemo.Core.Services;

public interface IToastService
{
    void Show(string title, string message, ToastType type = ToastType.Info, int durationMs = 5000);
    void Remove(string id);
    ObservableCollection<ToastNotification> PassiveToasts { get; }
    ObservableCollection<ToastNotification> StatusToasts { get; }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error,
    Process
}

public class ToastNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; }
    public string? TaskId { get; set; }
    public string? IconPath { get; set; }
    public double? Progress { get; set; }
    public string? ProgressText { get; set; }
    public bool Dismissable { get; set; } = true;
}
