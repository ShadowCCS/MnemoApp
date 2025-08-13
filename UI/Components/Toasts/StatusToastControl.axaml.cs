using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Toasts
{
    public partial class StatusToastControl : UserControl
    {
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
                        var svc = ApplicationHost.Services.GetService<IToastService>();
                        svc?.Remove(toast.Id);
                    }
                };
            }

            // Do not dismiss on root click; only the button should dismiss
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Intentionally no root click handler
    }
}


