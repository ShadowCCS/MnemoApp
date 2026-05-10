using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mnemo.UI.Modules.Notes.Views;

/// <summary>
/// Measures its child at the editor's normal layout width, then reports a scaled scroll extent.
/// This keeps camera zoom out of the editor's layout pass.
/// </summary>
public sealed class EditorZoomHost : Decorator
{
    private double _zoom = 1.0;
    private double _layoutWidth;

    public double Zoom
    {
        get => _zoom;
        set
        {
            var zoom = Math.Max(0.01, value);
            if (Math.Abs(_zoom - zoom) <= 1e-6)
                return;

            _zoom = zoom;
            InvalidateMeasure();
        }
    }

    public double LayoutWidth
    {
        get => _layoutWidth;
        set
        {
            var width = Math.Max(0, value);
            if (Math.Abs(_layoutWidth - width) <= 0.5)
                return;

            _layoutWidth = width;
            InvalidateMeasure();
        }
    }

    public Size NaturalSize { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null)
        {
            NaturalSize = default;
            return default;
        }

        var width = LayoutWidth > 1 ? LayoutWidth : availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 1)
            width = Child.DesiredSize.Width > 1 ? Child.DesiredSize.Width : 1;

        Child.Measure(new Size(width, double.PositiveInfinity));
        NaturalSize = Child.DesiredSize;

        return new Size(NaturalSize.Width * Zoom, NaturalSize.Height * Zoom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child == null)
            return finalSize;

        var natural = NaturalSize;
        if (natural.Width <= 1 || natural.Height <= 1)
            natural = Child.DesiredSize;

        Child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        Child.RenderTransform = new MatrixTransform(Matrix.CreateScale(Zoom, Zoom));
        Child.Arrange(new Rect(natural));

        return finalSize;
    }
}
