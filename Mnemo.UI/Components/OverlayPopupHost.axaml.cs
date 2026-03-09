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
                if (overlay.Options.AnchorControl != null || (overlay.Options.AnchorPointX.HasValue && overlay.Options.AnchorPointY.HasValue))
                {
                    var calculatedMargin = CalculateAnchorMargin(overlay, topLevel, null);
                    if (calculatedMargin.HasValue)
                    {
                        overlay.Options.Margin = calculatedMargin.Value;
                    }
                }
            }
        }

        private Thickness? CalculateAnchorMargin(OverlayInstance overlay, TopLevel topLevel, Size? overlaySize)
        {
            var offset = overlay.Options.AnchorOffset as Thickness? ?? new Thickness(0);
            double x, y;

            if (overlay.Options.AnchorPointX is { } px && overlay.Options.AnchorPointY is { } py)
            {
                if (!overlaySize.HasValue)
                    return null;
                var pos = overlay.Options.AnchorPosition;
                var w = overlaySize.Value.Width;
                var h = overlaySize.Value.Height;
                if (pos == AnchorPosition.TopCenter)
                {
                    x = px - w / 2;
                    y = py - h - (offset.Top <= 0 ? -offset.Top : 4);
                }
                else if (pos == AnchorPosition.BottomCenter)
                {
                    x = px - w / 2;
                    y = py + (offset.Top > 0 ? offset.Top : 4);
                }
                else
                {
                    x = px;
                    y = py;
                }
                return new Thickness(x + offset.Left, y + offset.Top, offset.Right, offset.Bottom);
            }

            var anchor = overlay.Options.AnchorControl as Control;
            if (anchor == null) return null;
            if (!anchor.IsVisible) return null;

            try
            {
                var anchorBounds = anchor.TranslatePoint(new Point(0, 0), topLevel);
                if (!anchorBounds.HasValue) return null;

                var anchorWidth = anchor.Bounds.Width;
                var anchorHeight = anchor.Bounds.Height;

                x = anchorBounds.Value.X;
                y = anchorBounds.Value.Y;

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

            var usePointAnchor = overlay.Options.AnchorPointX.HasValue && overlay.Options.AnchorPointY.HasValue;
            if (usePointAnchor && border.Bounds.Height <= 0)
            {
                Dispatcher.UIThread.Post(() => UpdateOverlayPosition(border, overlay), DispatcherPriority.Loaded);
                return;
            }

            if (overlay.Options.AnchorControl != null || usePointAnchor)
            {
                var overlaySize = usePointAnchor ? new Size(border.Bounds.Width, border.Bounds.Height) : (Size?)null;
                var calculatedMargin = CalculateAnchorMargin(overlay, topLevel, overlaySize);
                if (calculatedMargin.HasValue)
                {
                    var margin = calculatedMargin.Value;
                    // For TopLeft (control anchor only): position so overlay BOTTOM is just above the anchor (no overlap)
                    if (!usePointAnchor &&
                        overlay.Options.AnchorPosition == AnchorPosition.TopLeft &&
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
                            Dispatcher.UIThread.Post(() => UpdateOverlayPosition(border, overlay), DispatcherPriority.Loaded);
                            return;
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



