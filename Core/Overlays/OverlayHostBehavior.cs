using Avalonia;
using Avalonia.Controls;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;

namespace MnemoApp.Core.Overlays
{
    public static class OverlayHostBehavior
    {
        public static readonly AttachedProperty<bool> IsOverlayHostProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsOverlayHost", typeof(OverlayHostBehavior));

        public static void SetIsOverlayHost(AvaloniaObject element, bool value) => element.SetValue(IsOverlayHostProperty, value);
        public static bool GetIsOverlayHost(AvaloniaObject element) => element.GetValue(IsOverlayHostProperty);

        static OverlayHostBehavior()
        {
            IsOverlayHostProperty.Changed.AddClassHandler<Panel>((panel, args) =>
            {
                if (args.NewValue is bool enabled && enabled)
                {
                    // Prevent duplicate hosts
                    if (panel.Children.OfType<UI.Components.OverlayPopupHost>().Any())
                        return;

                    var host = new UI.Components.OverlayPopupHost
                    {
                        OverlayService = ApplicationHost.Services.GetService<IOverlayService>()
                    };
                    panel.Children.Add(host);
                }
                else
                {
                    // Remove existing hosts when disabled
                    var existing = panel.Children.OfType<UI.Components.OverlayPopupHost>().ToList();
                    foreach (var h in existing)
                        panel.Children.Remove(h);
                }
            });
        }
    }
}


