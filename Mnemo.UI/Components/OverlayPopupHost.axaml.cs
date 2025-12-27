using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core;
using Mnemo.Core.Services;
using System.Linq;

namespace Mnemo.UI.Components
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
                    var svc = ((App)Application.Current!).Services!.GetService<IOverlayService>();
                    if (svc != null) { OverlayService = svc; DataContext = svc; }
                }
                this.Focus();
            };

            // Subscribe to overlay changes to update anchor positions
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is IOverlayService service)
            {
                // Initial position update
                Dispatcher.UIThread.Post(() => UpdateAnchorPositions(), DispatcherPriority.Render);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void UpdateAnchorPositions()
        {
            if (OverlayService == null) return;

            var topLevel = this.FindAncestorOfType<TopLevel>();
            if (topLevel == null) return;

            foreach (var overlay in OverlayService.Overlays)
            {
                if (overlay.Options.AnchorControl != null)
                {
                    var calculatedMargin = CalculateAnchorMargin(overlay, topLevel);
                    if (calculatedMargin.HasValue)
                    {
                        overlay.Options.Margin = calculatedMargin.Value;
                    }
                }
            }
        }

        private Thickness? CalculateAnchorMargin(OverlayInstance overlay, TopLevel topLevel)
        {
            var anchor = overlay.Options.AnchorControl;
            if (anchor == null) return null;
            if (!anchor.IsVisible) return null;

            try
            {
                var anchorBounds = anchor!.TranslatePoint(new Point(0, 0), topLevel);
                if (!anchorBounds.HasValue) return null;

                var anchorWidth = anchor.Bounds.Width;
                var anchorHeight = anchor.Bounds.Height;
                var offset = overlay.Options.AnchorOffset as Thickness? ?? new Thickness(0);

                double x = anchorBounds.Value.X;
                double y = anchorBounds.Value.Y;

                switch (overlay.Options.AnchorPosition)
                {
                    case AnchorPosition.TopLeft:
                        y -= anchorHeight;
                        break;
                    case AnchorPosition.TopRight:
                        x += anchorWidth;
                        y -= anchorHeight;
                        break;
                    case AnchorPosition.BottomLeft:
                        y += anchorHeight;
                        break;
                    case AnchorPosition.BottomRight:
                        x += anchorWidth;
                        y += anchorHeight;
                        break;
                    case AnchorPosition.TopCenter:
                        x += anchorWidth / 2;
                        y -= anchorHeight;
                        break;
                    case AnchorPosition.BottomCenter:
                        x += anchorWidth / 2;
                        y += anchorHeight;
                        break;
                    case AnchorPosition.LeftCenter:
                        x -= anchorWidth;
                        y += anchorHeight / 2;
                        break;
                    case AnchorPosition.RightCenter:
                        x += anchorWidth;
                        y += anchorHeight / 2;
                        break;
                }

                return new Thickness(
                    x + offset.Left,
                    y + offset.Top,
                    offset.Right,
                    offset.Bottom
                );
            }
            catch
            {
                return null;
            }
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

        private void OnOverlayLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is OverlayInstance overlay)
            {
                UpdateOverlayPosition(border, overlay);
            }
        }

        private void OnOverlayLayoutUpdated(object? sender, EventArgs e)
        {
            if (sender is Border border && border.DataContext is OverlayInstance overlay)
            {
                UpdateOverlayPosition(border, overlay);
            }
        }

        private void UpdateOverlayPosition(Border border, OverlayInstance overlay)
        {
            if (overlay.Options.AnchorControl != null)
            {
                var topLevel = this.FindAncestorOfType<TopLevel>();
                if (topLevel != null)
                {
                    var calculatedMargin = CalculateAnchorMargin(overlay, topLevel);
                    if (calculatedMargin.HasValue && border.Margin != calculatedMargin.Value)
                    {
                        // Set margin directly on border to bypass binding issues
                        border.Margin = calculatedMargin.Value;
                        // Also update options for consistency
                        overlay.Options.Margin = calculatedMargin.Value;
                        // Use Left/Top alignment when anchored
                        border.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                        border.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                    }
                }
            }
        }
    }
}



