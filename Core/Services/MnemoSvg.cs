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
        public static readonly StyledProperty<Color> FillColorProperty =
            AvaloniaProperty.Register<MnemoSvg, Color>(nameof(FillColor), Colors.Transparent);

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
        private string? _originalSvgContent;

        static MnemoSvg()
        {
            // Listen for property changes to invalidate visual
            FillColorProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            StrokeColorProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            StrokeWidthProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnPropertiesChanged());
            SvgPathProperty.Changed.AddClassHandler<MnemoSvg>((x, e) => x.OnSvgPathChanged());
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
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
            LoadSvg();
            InvalidateVisual();
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
                    // Only replace fill attribute if FillColor is explicitly set (not transparent)
                    if (FillColor.A > 0)
                    {
                        var fillHex = $"#{FillColor.R:X2}{FillColor.G:X2}{FillColor.B:X2}";
                        element.SetAttributeValue("fill", fillHex);
                        
                        if (FillColor.A < 255)
                        {
                            var fillOpacity = FillColor.A / 255.0;
                            element.SetAttributeValue("fill-opacity", fillOpacity.ToString("F3"));
                        }
                        else
                        {
                            element.Attribute("fill-opacity")?.Remove();
                        }
                    }
                    // If FillColor is transparent (default), preserve existing fill attributes

                    // Replace stroke attributes if StrokeWidth > 0
                    if (StrokeWidth > 0)
                    {
                        element.SetAttributeValue("stroke-width", StrokeWidth.ToString());
                        
                        if (StrokeColor.A > 0)
                        {
                            var strokeHex = $"#{StrokeColor.R:X2}{StrokeColor.G:X2}{StrokeColor.B:X2}";
                            element.SetAttributeValue("stroke", strokeHex);
                            
                            if (StrokeColor.A < 255)
                            {
                                var strokeOpacity = StrokeColor.A / 255.0;
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
                        // Remove stroke if width is 0
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

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_svg?.Picture is null)
                return new Size(0, 0);

            var svgBounds = _svg.Picture.CullRect;
            return new Size(svgBounds.Width, svgBounds.Height);
        }
    }
}