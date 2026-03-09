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
            var anchor = overlay.Options.AnchorControl as Control;
            if (anchor == null) return null;
            if (!anchor.IsVisible) return null;

            try
            {
                var anchorBounds = anchor.TranslatePoint(new Point(0, 0), topLevel);
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
                if (top != null && top.Options.CloseOnEscape)
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
            var topLevel = this.FindAncestorOfType<TopLevel>();
            if (topLevel == null) return;

            if (overlay.Options.AnchorControl != null)
            {
                var calculatedMargin = CalculateAnchorMargin(overlay, topLevel);
                if (calculatedMargin.HasValue)
                {
                    var margin = calculatedMargin.Value;
                    // For TopLeft: position so overlay BOTTOM is just above the anchor (no overlap)
                    if (overlay.Options.AnchorPosition == AnchorPosition.TopLeft &&
                        overlay.Options.AnchorControl is Control anchor &&
                        anchor.TranslatePoint(new Point(0, 0), topLevel) is { } anchorPt)
                    {
                        if (border.Bounds.Height > 0)
                        {
                            var offset = overlay.Options.AnchorOffset as Thickness? ?? new Thickness(0);
                            double gap = offset.Top <= 0 ? -offset.Top : 4;
                            margin = new Thickness(margin.Left, anchorPt.Y - border.Bounds.Height - gap, margin.Right, margin.Bottom);
                        }
                        else
                        {
                            // Height not yet available; re-run after layout so we can position correctly
                            Dispatcher.UIThread.Post(() => UpdateOverlayPosition(border, overlay), DispatcherPriority.Loaded);
                        }
                    }
                    // Prevent layout loops by using RenderTransform for anchored overlays instead of Margin.
                    // Margin affects available size for layout, which can cause the border's Height to shrink if pushed off-screen,
                    // which changes the Margin again (if TopLeft), causing an infinite layout loop.
                    var transform = border.RenderTransform as Avalonia.Media.TranslateTransform;
                    if (transform == null)
                    {
                        transform = new Avalonia.Media.TranslateTransform();
                        border.RenderTransform = transform;
                    }

                    if (Math.Abs(transform.X - margin.Left) > 0.1 || Math.Abs(transform.Y - margin.Top) > 0.1)
                    {
                        transform.X = margin.Left;
                        transform.Y = margin.Top;
                    }

                    // Reset Margin to 0 so it doesn't constrain the popup and cause it to shrink when pushed near window edges
                    if (border.Margin != new Thickness(0))
                        border.Margin = new Thickness(0);

                    if (border.HorizontalAlignment != Avalonia.Layout.HorizontalAlignment.Left)
                        border.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;

                    if (border.VerticalAlignment != Avalonia.Layout.VerticalAlignment.Top)
                        border.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                }
            }
            else
            {
                // Handle non-anchored overlay positioning
                if (overlay.Options.HorizontalAlignment is string hStr && Enum.TryParse<HorizontalAlignment>(hStr, true, out var hAlign))
                {
                    if (border.HorizontalAlignment != hAlign) border.HorizontalAlignment = hAlign;
                }
                else if (overlay.Options.HorizontalAlignment is HorizontalAlignment hEnum)
                {
                    if (border.HorizontalAlignment != hEnum) border.HorizontalAlignment = hEnum;
                }

                if (overlay.Options.VerticalAlignment is string vStr && Enum.TryParse<VerticalAlignment>(vStr, true, out var vAlign))
                {
                    if (border.VerticalAlignment != vAlign) border.VerticalAlignment = vAlign;
                }
                else if (overlay.Options.VerticalAlignment is VerticalAlignment vEnum)
                {
                    if (border.VerticalAlignment != vEnum) border.VerticalAlignment = vEnum;
                }

                if (overlay.Options.Margin is string mStr)
                {
                    try 
                    { 
                        var parsed = Thickness.Parse(mStr); 
                        if (border.Margin != parsed) border.Margin = parsed;
                    }
                    catch { /* Ignore invalid thickness strings */ }
                }
                else if (overlay.Options.Margin is Thickness mThickness)
                {
                    if (border.Margin != mThickness) border.Margin = mThickness;
                }
            }
        }
    }
}



