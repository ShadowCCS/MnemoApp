using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using System;

namespace Mnemo.UI.Controls
{
    public class SvgIcon : Control
{
    public static readonly StyledProperty<IBrush?> ColorProperty =
        AvaloniaProperty.Register<SvgIcon, IBrush?>(nameof(Color));

    public static readonly StyledProperty<string?> SvgPathProperty =
        AvaloniaProperty.Register<SvgIcon, string?>(nameof(SvgPath));

    public IBrush? Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public string? SvgPath { get => GetValue(SvgPathProperty); set => SetValue(SvgPathProperty, value); }

    private SKPicture? _svgPicture;
    private string? _loadedPath;

    static SvgIcon()
    {
        ColorProperty.Changed.AddClassHandler<SvgIcon>((x, e) => x.InvalidateVisual());
        SvgPathProperty.Changed.AddClassHandler<SvgIcon>((x, e) =>
        {
            x._svgPicture?.Dispose();
            x._svgPicture = null;
            x._loadedPath = null;
            x.InvalidateVisual();
        });
    }

    private void LoadSvg(string path)
    {
        if (_loadedPath == path && _svgPicture != null) return;
        
        try
        {
            using var stream = AssetLoader.Open(new Uri(path));
            var svg = new SKSvg();
            _svgPicture = svg.Load(stream);
            _loadedPath = path;
        }
        catch { }
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (string.IsNullOrEmpty(SvgPath)) return;

        LoadSvg(SvgPath);
        if (_svgPicture == null) return;

        SKColor? color = null;
        if (Color is ISolidColorBrush solidBrush)
        {
            var c = solidBrush.Color;
            color = new SKColor(c.R, c.G, c.B, c.A);
        }

        context.Custom(new SvgIconDrawOperation(_svgPicture, Bounds, color));
    }

    private class SvgIconDrawOperation : ICustomDrawOperation
    {
        private readonly SKPicture _picture;
        private readonly Rect _bounds;
        private readonly SKColor? _color;

        public SvgIconDrawOperation(SKPicture picture, Rect bounds, SKColor? color)
        {
            _picture = picture;
            _bounds = bounds;
            _color = color;
        }

        public void Dispose() { }
        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => _bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.PlatformImpl.GetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            canvas.Save();

            var pictureRect = _picture.CullRect;
            float scaleX = (float)_bounds.Width / pictureRect.Width;
            float scaleY = (float)_bounds.Height / pictureRect.Height;
            float scale = Math.Min(scaleX, scaleY);

            // Center the icon within bounds
            float scaledWidth = pictureRect.Width * scale;
            float scaledHeight = pictureRect.Height * scale;
            float offsetX = ((float)_bounds.Width - scaledWidth) / 2;
            float offsetY = ((float)_bounds.Height - scaledHeight) / 2;

            canvas.Translate(offsetX, offsetY);
            canvas.Scale(scale);

            using var paint = new SKPaint();
            if (_color.HasValue)
            {
                paint.ColorFilter = SKColorFilter.CreateBlendMode(_color.Value, SKBlendMode.SrcIn);
            }

            canvas.DrawPicture(_picture, paint);
            canvas.Restore();
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is SvgIconDrawOperation op &&
                   ReferenceEquals(op._picture, _picture) &&
                   op._bounds == _bounds &&
                   op._color == _color;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _svgPicture?.Dispose();
        _svgPicture = null;
        base.OnDetachedFromVisualTree(e);
    }
}
}