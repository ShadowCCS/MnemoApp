using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;

namespace Mnemo.UI.ViewModels;

public partial class ToastItemViewModel : ObservableObject, IDisposable
{
    private readonly Action<Guid> _remove;
    private readonly DispatcherTimer? _timer;
    private readonly TimeSpan _duration;
    private DateTime _endsAtUtc;
    private readonly Action? _onPrimary;
    private readonly Action? _onSecondary;
    private readonly Action? _onDismissed;
    private readonly bool _dismissAfterPrimary;
    private readonly bool _dismissAfterSecondary;

    public Guid Id { get; }
    public ToastType ToastType { get; }
    public string Title { get; }
    public string Description { get; }

    public string? PrimaryActionLabel { get; }
    public string? SecondaryActionLabel { get; }

    public bool HasPrimaryAction => !string.IsNullOrWhiteSpace(PrimaryActionLabel);
    public bool HasSecondaryAction => !string.IsNullOrWhiteSpace(SecondaryActionLabel);
    public bool HasAnyAction => HasPrimaryAction || HasSecondaryAction;

    [ObservableProperty]
    private double _timerProgress = 1;

    [ObservableProperty]
    private bool _showTimerBar = true;

    public bool IsInfo => ToastType == ToastType.Info;
    public bool IsSuccess => ToastType == ToastType.Success;
    public bool IsWarning => ToastType == ToastType.Warning;
    public bool IsAction => ToastType == ToastType.Action;
    public bool IsTask => ToastType == ToastType.Task;

    /// <summary>Style class for the timer strip; one ProgressBar gets per-type Foreground from styles.</summary>
    public string ToastTimerClass => ToastType switch
    {
        ToastType.Info => "toast-timer-info",
        ToastType.Success => "toast-timer-success",
        ToastType.Warning => "toast-timer-warning",
        ToastType.Action => "toast-timer-action",
        ToastType.Task => "toast-timer-task",
        _ => "toast-timer-info",
    };

    public string IconSvgPath => ToastType switch
    {
        ToastType.Info => "avares://Mnemo.UI/Icons/Toast/system_info.svg",
        ToastType.Success => "avares://Mnemo.UI/Icons/Toast/system_success.svg",
        ToastType.Warning => "avares://Mnemo.UI/Icons/Toast/system_warning.svg",
        ToastType.Action => "avares://Mnemo.UI/Icons/Toast/system_error.svg",
        ToastType.Task => "avares://Mnemo.UI/Icons/Toast/system_process.svg",
        _ => "avares://Mnemo.UI/Icons/Toast/system_info.svg",
    };

    public ToastItemViewModel(
        Guid id,
        ToastType toastType,
        string title,
        string description,
        TimeSpan duration,
        Action<Guid> remove,
        ToastActionSpec? actions = null)
    {
        Id = id;
        ToastType = toastType;
        Title = title;
        Description = description;
        _remove = remove;

        PrimaryActionLabel = actions?.PrimaryLabel;
        SecondaryActionLabel = actions?.SecondaryLabel;
        _onPrimary = actions?.OnPrimary;
        _onSecondary = actions?.OnSecondary;
        _onDismissed = actions?.OnDismissed;
        _dismissAfterPrimary = actions?.DismissAfterPrimary ?? true;
        _dismissAfterSecondary = actions?.DismissAfterSecondary ?? true;

        if (duration <= TimeSpan.Zero)
        {
            ShowTimerBar = false;
            return;
        }

        _duration = duration < TimeSpan.FromMilliseconds(400) ? TimeSpan.FromMilliseconds(400) : duration;
        _endsAtUtc = DateTime.UtcNow + _duration;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var leftMs = (_endsAtUtc - DateTime.UtcNow).TotalMilliseconds;
        var totalMs = _duration.TotalMilliseconds;
        TimerProgress = Math.Max(0, Math.Min(1, leftMs / totalMs));
        if (leftMs <= 0)
        {
            _timer?.Stop();
            _remove(Id);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _onDismissed?.Invoke();
        _remove(Id);
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        _onPrimary?.Invoke();
        if (_dismissAfterPrimary)
            _remove(Id);
    }

    [RelayCommand]
    private void SecondaryAction()
    {
        _onSecondary?.Invoke();
        if (_dismissAfterSecondary)
            _remove(Id);
    }

    public void Dispose()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
