using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Mnemo.UI.Controls
{
    /// <summary>
    /// A control that renders an SVG icon with optional color tinting.
    /// </summary>
    public class SvgIcon : Control
    {
        private static readonly ConcurrentDictionary<string, RefCountedPicture> _svgCache = new();

        private class RefCountedPicture
        {
            private SKPicture? _picture;
            private int _refCount;

            public RefCountedPicture(SKPicture picture)
            {
                _picture = picture;
                _refCount = 1;
            }

            public SKPicture Picture => _picture ?? throw new ObjectDisposedException(nameof(RefCountedPicture));

            public void Increment() => Interlocked.Increment(ref _refCount);

            public void Decrement()
            {
                if (Interlocked.Decrement(ref _refCount) == 0)
                {
                    _picture?.Dispose();
                    _picture = null;
                }
            }
        }

        public static readonly StyledProperty<IBrush?> ColorProperty =
        AvaloniaProperty.Register<SvgIcon, IBrush?>(nameof(Color));

    public static readonly StyledProperty<string?> SvgPathProperty =
        AvaloniaProperty.Register<SvgIcon, string?>(nameof(SvgPath));

    public IBrush? Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public string? SvgPath { get => GetValue(SvgPathProperty); set => SetValue(SvgPathProperty, value); }

        private RefCountedPicture? _svgPicture;
        private string? _loadedPath;

        static SvgIcon()
        {
            ColorProperty.Changed.AddClassHandler<SvgIcon>((x, e) => x.InvalidateVisual());
            SvgPathProperty.Changed.AddClassHandler<SvgIcon>((x, e) =>
            {
                x._svgPicture?.Decrement();
                x._svgPicture = null;
                x._loadedPath = null;
                x.InvalidateVisual();
            });
        }

        private void LoadSvg(string path)
        {
            if (_loadedPath == path && _svgPicture != null) return;

            // Clean up existing reference
            _svgPicture?.Decrement();
            _svgPicture = null;
            _loadedPath = null;

            // Check cache first
            if (_svgCache.TryGetValue(path, out var cached))
            {
                cached.Increment();
                _svgPicture = cached;
                _loadedPath = path;
                return;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(path));
                var svg = new SKSvg();
                var picture = svg.Load(stream);
                
                if (picture != null)
                {
                    var wrapped = new RefCountedPicture(picture);
                    // Try to add to cache (initial refCount is 1 for the cache)
                    if (_svgCache.TryAdd(path, wrapped))
                    {
                        wrapped.Increment(); // Increment for this instance's ownership (refCount = 2)
                        _svgPicture = wrapped;
                    }
                    else
                    {
                        // Someone else added it while we were loading
                        wrapped.Decrement(); // Dispose our local one (refCount = 0)
                        if (_svgCache.TryGetValue(path, out var otherCached))
                        {
                            otherCached.Increment();
                            _svgPicture = otherCached;
                        }
                    }
                }
                _loadedPath = path;
            }
            catch (Exception ex)
            {
                // Log error for debugging - SVG loading failed
                System.Diagnostics.Debug.WriteLine($"Failed to load SVG from {path}: {ex.Message}");
            }
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

            _svgPicture.Increment();
            context.Custom(new SvgIconDrawOperation(_svgPicture, Bounds, color));
        }

        private class SvgIconDrawOperation : ICustomDrawOperation
        {
            private readonly RefCountedPicture _pictureWrapper;
            private readonly Rect _bounds;
            private readonly SKColor? _color;

            public SvgIconDrawOperation(RefCountedPicture pictureWrapper, Rect bounds, SKColor? color)
            {
                _pictureWrapper = pictureWrapper;
                _bounds = bounds;
                _color = color;
            }

            public void Dispose() => _pictureWrapper.Decrement();
            public Rect Bounds => _bounds;
            public bool HitTest(Point p) => _bounds.Contains(p);

            public void Render(ImmediateDrawingContext context)
            {
                var leaseFeature = context.PlatformImpl.GetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature == null) return;

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;

                canvas.Save();

                var picture = _pictureWrapper.Picture;
                var pictureRect = picture.CullRect;
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

                if (_color.HasValue)
                {
                    using var paint = new SKPaint();
                    paint.ColorFilter = SKColorFilter.CreateBlendMode(_color.Value, SKBlendMode.SrcIn);
                    canvas.DrawPicture(picture, paint);
                }
                else
                {
                    canvas.DrawPicture(picture);
                }
                canvas.Restore();
            }

            public bool Equals(ICustomDrawOperation? other)
            {
                return other is SvgIconDrawOperation op &&
                       ReferenceEquals(op._pictureWrapper, _pictureWrapper) &&
                       op._bounds == _bounds &&
                       op._color == _color;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _svgPicture?.Decrement();
            _svgPicture = null;
            base.OnDetachedFromVisualTree(e);
        }
}
}