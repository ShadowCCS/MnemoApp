using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using System;
using System.IO;

namespace MnemoApp.Core.Services
{
    public class MnemoSvg : Control
    {
        // Dependency Properties
        public static readonly StyledProperty<Color> FillColorProperty =
            AvaloniaProperty.Register<MnemoSvg, Color>(nameof(FillColor), Colors.Black);

        public static readonly StyledProperty<Color> StrokeColorProperty =
            AvaloniaProperty.Register<MnemoSvg, Color>(nameof(StrokeColor), Colors.Transparent);

        public static readonly StyledProperty<double> StrokeWidthProperty =
            AvaloniaProperty.Register<MnemoSvg, double>(nameof(StrokeWidth), 0.0);

        public static readonly StyledProperty<string?> SvgPathProperty =
            AvaloniaProperty.Register<MnemoSvg, string?>(nameof(SvgPath));

        // Properties
        public Color FillColor
        {
            get => GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }

        public Color StrokeColor
        {
            get => GetValue(StrokeColorProperty);
            set => SetValue(StrokeColorProperty, value);
        }

        public double StrokeWidth
        {
            get => GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }

        public string? SvgPath
        {
            get => GetValue(SvgPathProperty);
            set => SetValue(SvgPathProperty, value);
        }

        private SKSvg? _svg;

        static MnemoSvg()
        {
            // Listen for property changes to invalidate visual
            FillColorProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.InvalidateVisual());
            StrokeColorProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.InvalidateVisual());
            StrokeWidthProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.InvalidateVisual());
            SvgPathProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnSvgPathChanged());
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            LoadSvg();
        }

        private void OnSvgPathChanged()
        {
            LoadSvg();
            InvalidateVisual();
        }

        private void LoadSvg()
        {
            if (string.IsNullOrEmpty(SvgPath))
            {
                _svg = null;
                return;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(SvgPath));
                _svg = new SKSvg();
                _svg.Load(stream);
            }
            catch
            {
                _svg = null;
            }
        }

        public override void Render(DrawingContext context)
        {
            if (_svg?.Picture is null || Bounds.Width <= 0 || Bounds.Height <= 0) 
                return;

            var width = (int)Bounds.Width;
            var height = (int)Bounds.Height;

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            
            canvas.Clear(SKColors.Transparent);

            // Scale SVG to fit the control bounds
            var svgBounds = _svg.Picture.CullRect;
            var scaleX = width / svgBounds.Width;
            var scaleY = height / svgBounds.Height;
            var scale = Math.Min(scaleX, scaleY);

            canvas.Scale(scale);

            // Apply color and stroke modifications
            using var paint = new SKPaint();
            
            // Create a color filter for fill color if it's not transparent
            if (FillColor.A > 0)
            {
                var fillColorFilter = SKColorFilter.CreateBlendMode(
                    new SKColor(FillColor.R, FillColor.G, FillColor.B, FillColor.A),
                    SKBlendMode.SrcIn);
                paint.ColorFilter = fillColorFilter;
            }

            // Draw the SVG
            canvas.DrawPicture(_svg.Picture, paint);

            // If stroke is enabled, draw stroke overlay
            if (StrokeWidth > 0 && StrokeColor.A > 0)
            {
                using var strokePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)StrokeWidth,
                    Color = new SKColor(StrokeColor.R, StrokeColor.G, StrokeColor.B, StrokeColor.A),
                    IsAntialias = true
                };

                canvas.DrawPicture(_svg.Picture, strokePaint);
            }

            // Convert to Avalonia bitmap and draw
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            context.DrawImage(bitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_svg?.Picture is null)
                return new Size(0, 0);

            var svgBounds = _svg.Picture.CullRect;
            return new Size(svgBounds.Width, svgBounds.Height);
        }
    }
}