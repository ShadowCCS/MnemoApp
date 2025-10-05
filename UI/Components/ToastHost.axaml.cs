using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components
{
    public partial class ToastHost : UserControl
    {
        public static readonly StyledProperty<IToastService?> ToastServiceProperty =
            AvaloniaProperty.Register<ToastHost, IToastService?>(nameof(ToastService));

        public IToastService? ToastService
        {
            get => GetValue(ToastServiceProperty);
            set => SetValue(ToastServiceProperty, value);
        }

        public ToastHost()
        {
            InitializeComponent();
            this.AttachedToVisualTree += (_, __) =>
            {
                if (ToastService != null) 
                { 
                    DataContext = ToastService;
                }
                else
                {
                    // Fallback for dynamic creation
                    var svc = ApplicationHost.GetServiceProvider().GetService<IToastService>();
                    if (svc != null) { ToastService = svc; DataContext = svc; }
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}


