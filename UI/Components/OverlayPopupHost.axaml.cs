using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Overlays;
using System.Linq;

namespace MnemoApp.UI.Components
{
    public partial class OverlayPopupHost : UserControl
    {
        public static readonly StyledProperty<IOverlayService?> OverlayServiceProperty =
            AvaloniaProperty.Register<OverlayPopupHost, IOverlayService?>(nameof(OverlayService));

        public IOverlayService? OverlayService
        {
            get => GetValue(OverlayServiceProperty);
            set => SetValue(OverlayServiceProperty, value);
        }

        public OverlayPopupHost()
        {
            InitializeComponent();
            this.AttachedToVisualTree += (_, __) =>
            {
                if (OverlayService != null) 
                { 
                    DataContext = OverlayService;
                }
                else
                {
                    // Fallback for dynamic creation (e.g., OverlayHostBehavior)
                    var svc = ApplicationHost.GetServiceProvider().GetService<IOverlayService>();
                    if (svc != null) { OverlayService = svc; DataContext = svc; }
                }
                this.Focus();
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void BackdropPressed(object? sender, PointerPressedEventArgs e)
        {
            if (OverlayService == null) return;
            if (sender is Border border && border.DataContext is OverlayInstance instance)
            {
                if (instance.Options.CloseOnOutsideClick)
                {
                    OverlayService.CloseOverlay(instance.Id);
                    e.Handled = true;
                }
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && OverlayService != null)
            {
                var top = OverlayService.Overlays.OrderBy(o => o.ZIndex).LastOrDefault();
                if (top != null)
                {
                    OverlayService.CloseOverlay(top.Id);
                    e.Handled = true;
                }
            }
        }
    }
}


