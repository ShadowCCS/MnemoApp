using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components;

public partial class ToastItemView : UserControl
{
    private static readonly string[] ToastTimerClassKeys =
    [
        "toast-timer-info",
        "toast-timer-success",
        "toast-timer-warning",
        "toast-timer-action",
        "toast-timer-task",
    ];

    public ToastItemView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ApplyToastTimerClass();
    }

    private void ApplyToastTimerClass()
    {
        if (ToastTimerBar == null || DataContext is not ToastItemViewModel vm)
            return;
        foreach (var k in ToastTimerClassKeys)
            ToastTimerBar.Classes.Remove(k);
        ToastTimerBar.Classes.Add(vm.ToastTimerClass);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyToastTimerClass();
        RootBorder.RenderTransform = new TranslateTransform(56, 0);
        Dispatcher.UIThread.Post(() => RootBorder.RenderTransform = new TranslateTransform(0, 0), DispatcherPriority.Loaded);
    }
}
