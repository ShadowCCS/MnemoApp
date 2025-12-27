using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core;
using Mnemo.Core.Services;
using Mnemo.Core.Services;

namespace Mnemo.UI.Components.Toasts
{
    public partial class StatusToastControl : UserControl
    {
        public static readonly StyledProperty<IToastService?> ToastServiceProperty =
            AvaloniaProperty.Register<StatusToastControl, IToastService?>(nameof(ToastService));
        
        public IToastService? ToastService
        {
            get => GetValue(ToastServiceProperty);
            set => SetValue(ToastServiceProperty, value);
        }

        public StatusToastControl()
        {
            InitializeComponent();
            var dismiss = this.FindControl<Button>("DismissButton");
            if (dismiss != null)
            {
                dismiss.Click += (_, __) =>
                {
                    if (DataContext is ToastNotification toast)
                    {
                        var service = ToastService ?? ((App)Application.Current!).Services!.GetService<IToastService>();
                        service?.Remove(toast.Id);
                    }
                };
            }
            // Open loading overlay when clicking anywhere on the toast (except dismiss button)
            var root = this.FindControl<Control>("ToastRoot");
            if (root != null)
            {
                root.PointerReleased += (_, __) =>
                {
                    if (DataContext is ToastNotification toast && !string.IsNullOrEmpty(toast.TaskId))
                    {
                        try 
                        { 
                            var loading = ((App)Application.Current!).Services!.GetService<ILoadingService>();
                            loading?.ShowForTask(toast.TaskId); 
                        } 
                        catch { }
                    }
                };
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

    }
}



