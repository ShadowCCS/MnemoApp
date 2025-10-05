using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Services;
using MnemoApp.Core.MnemoAPI;

namespace MnemoApp.UI.Components.Toasts
{
    public partial class StatusToastControl : UserControl
    {
        public static readonly StyledProperty<IToastService?> ToastServiceProperty =
            AvaloniaProperty.Register<StatusToastControl, IToastService?>(nameof(ToastService));
        
        public static readonly StyledProperty<IMnemoAPI?> MnemoAPIProperty =
            AvaloniaProperty.Register<StatusToastControl, IMnemoAPI?>(nameof(MnemoAPI));

        public IToastService? ToastService
        {
            get => GetValue(ToastServiceProperty);
            set => SetValue(ToastServiceProperty, value);
        }

        public IMnemoAPI? MnemoAPI
        {
            get => GetValue(MnemoAPIProperty);
            set => SetValue(MnemoAPIProperty, value);
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
                        var service = ToastService ?? ApplicationHost.GetServiceProvider().GetService<IToastService>();
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
                    if (DataContext is ToastNotification toast && toast.TaskId.HasValue)
                    {
                        try 
                        { 
                            var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetService<IMnemoAPI>();
                            api?.ui.loading.showForTask(toast.TaskId.Value); 
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


