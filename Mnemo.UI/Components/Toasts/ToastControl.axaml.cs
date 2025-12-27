using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core;
using Mnemo.Core.Services;

namespace Mnemo.UI.Components.Toasts
{
    public partial class ToastControl : UserControl
    {
        public static readonly StyledProperty<IToastService?> ToastServiceProperty =
            AvaloniaProperty.Register<ToastControl, IToastService?>(nameof(ToastService));

        public IToastService? ToastService
        {
            get => GetValue(ToastServiceProperty);
            set => SetValue(ToastServiceProperty, value);
        }

        public ToastControl()
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

        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

    }
}



