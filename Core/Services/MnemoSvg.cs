using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Skia;
using System.Text;
using SkiaSharp;
using Svg.Skia;
using System;
using System.IO;
using System.Xml.Linq;
using System.Linq;

namespace MnemoApp.Core.Services
{
    public class MnemoSvg : Control
    {
        // Dependency Properties
        public static readonly StyledProperty<IBrush?> FillProperty =
            AvaloniaProperty.Register<MnemoSvg, IBrush?>(nameof(Fill), Brushes.Transparent);

        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<MnemoSvg, IBrush?>(nameof(Stroke), Brushes.Transparent);

        public static readonly StyledProperty<double> StrokeWidthProperty =
            AvaloniaProperty.Register<MnemoSvg, double>(nameof(StrokeWidth), 0.0);

        public static readonly StyledProperty<string?> SvgPathProperty =
            AvaloniaProperty.Register<MnemoSvg, string?>(nameof(SvgPath));

        // Properties
        public IBrush? Fill
        {
            get => GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public IBrush? Stroke
        {
            get => GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
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
        private string? _originalSvgContent;
        private IDisposable? _fillBrushSubscription;
        private IDisposable? _strokeBrushSubscription;

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? _dispose;
            public CallbackDisposable(Action dispose) => _dispose = dispose;
            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }

        static MnemoSvg()
        {
            // Listen for property changes to invalidate visual
            FillProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            StrokeProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            StrokeWidthProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            SvgPathProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnSvgPathChanged());
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            SubscribeToBrushChanges();
            LoadSvg();
        }

        private void OnSvgPathChanged()
        {
            LoadOriginalSvg();
            LoadSvg();
            InvalidateVisual();
        }

        private void OnPropertiesChanged()
        {
            SubscribeToBrushChanges();
            LoadSvg();
            InvalidateVisual();
        }

        private void SubscribeToBrushChanges()
        {
            _fillBrushSubscription?.Dispose();
            _strokeBrushSubscription?.Dispose();

            if (Fill is ISolidColorBrush solidFill && solidFill is AvaloniaObject aoFill)
            {
                EventHandler<AvaloniaPropertyChangedEventArgs>? handler = (s, e) =>
                {
                    if (e.Property == SolidColorBrush.ColorProperty)
                    {
                        LoadSvg();
                        InvalidateVisual();
                    }
                };
                aoFill.PropertyChanged += handler;
                _fillBrushSubscription = new CallbackDisposable(() => aoFill.PropertyChanged -= handler);
            }

            if (Stroke is ISolidColorBrush solidStroke && solidStroke is AvaloniaObject aoStroke)
            {
                EventHandler<AvaloniaPropertyChangedEventArgs>? handler = (s, e) =>
                {
                    if (e.Property == SolidColorBrush.ColorProperty)
                    {
                        LoadSvg();
                        InvalidateVisual();
                    }
                };
                aoStroke.PropertyChanged += handler;
                _strokeBrushSubscription = new CallbackDisposable(() => aoStroke.PropertyChanged -= handler);
            }
        }

        private void LoadOriginalSvg()
        {
            if (string.IsNullOrEmpty(SvgPath))
            {
                _originalSvgContent = null;
                return;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(SvgPath));
                using var reader = new StreamReader(stream);
                _originalSvgContent = reader.ReadToEnd();
            }
            catch
            {
                _originalSvgContent = null;
            }
        }

        private void LoadSvg()
        {
            if (string.IsNullOrEmpty(_originalSvgContent))
            {
                _svg = null;
                return;
            }

            try
            {
                var modifiedSvg = ModifySvgContent(_originalSvgContent);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(modifiedSvg));
                _svg = new SKSvg();
                _svg.Load(stream);
            }
            catch
            {
                _svg = null;
            }
        }

        private string ModifySvgContent(string originalSvg)
        {
            try
            {
                var doc = XDocument.Parse(originalSvg);

                // Find all drawable elements
                var elements = doc.Descendants()
                    .Where(e => e.Name.LocalName == "path" || 
                               e.Name.LocalName == "circle" || 
                               e.Name.LocalName == "rect" || 
                               e.Name.LocalName == "ellipse" ||
                               e.Name.LocalName == "polygon" ||
                               e.Name.LocalName == "polyline");

                foreach (var element in elements)
                {
                    // Handle fill brush
                    if (Fill is ISolidColorBrush fillBrush && fillBrush != Brushes.Transparent)
                    {
                        var fillColor = fillBrush.Color;
                        var fillHex = $"#{fillColor.R:X2}{fillColor.G:X2}{fillColor.B:X2}";
                        element.SetAttributeValue("fill", fillHex);
                        
                        if (fillColor.A < 255)
                        {
                            var fillOpacity = fillColor.A / 255.0;
                            element.SetAttributeValue("fill-opacity", fillOpacity.ToString("F3"));
                        }
                        else
                        {
                            element.Attribute("fill-opacity")?.Remove();
                        }
                    }
                    else if (Fill == null || Fill == Brushes.Transparent)
                    {
                        // Remove fill attributes if brush is null or transparent
                        element.Attribute("fill")?.Remove();
                        element.Attribute("fill-opacity")?.Remove();
                    }

                    // Handle stroke brush and width
                    if (StrokeWidth > 0)
                    {
                        element.SetAttributeValue("stroke-width", StrokeWidth.ToString());
                        
                        if (Stroke is ISolidColorBrush strokeBrush && strokeBrush != Brushes.Transparent)
                        {
                            var strokeColor = strokeBrush.Color;
                            var strokeHex = $"#{strokeColor.R:X2}{strokeColor.G:X2}{strokeColor.B:X2}";
                            element.SetAttributeValue("stroke", strokeHex);
                            
                            if (strokeColor.A < 255)
                            {
                                var strokeOpacity = strokeColor.A / 255.0;
                                element.SetAttributeValue("stroke-opacity", strokeOpacity.ToString("F3"));
                            }
                            else
                            {
                                element.Attribute("stroke-opacity")?.Remove();
                            }
                        }
                    }
                    else
                    {
                        // Remove stroke attributes if width is 0
                        element.Attribute("stroke")?.Remove();
                        element.Attribute("stroke-width")?.Remove();
                        element.Attribute("stroke-opacity")?.Remove();
                    }
                }

                return doc.ToString();
            }
            catch
            {
                // If XML parsing fails, return original
                return originalSvg;
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

            // Draw the SVG (colors and stroke are now baked into the SVG)
            canvas.DrawPicture(_svg.Picture);

            // Convert to Avalonia bitmap and draw
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            context.DrawImage(bitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _fillBrushSubscription?.Dispose();
            _strokeBrushSubscription?.Dispose();
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