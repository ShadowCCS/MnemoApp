using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace MnemoApp.Core.Services
{
    // Lightweight preview renderer that swaps three fills in a static SVG template
    public class ThemePreviewSvg : Control
    {
        public static readonly StyledProperty<string?> SvgPathProperty =
            AvaloniaProperty.Register<ThemePreviewSvg, string?>(nameof(SvgPath));

        public static readonly StyledProperty<string?> Color1Property =
            AvaloniaProperty.Register<ThemePreviewSvg, string?>(nameof(Color1));
        public static readonly StyledProperty<string?> Color2Property =
            AvaloniaProperty.Register<ThemePreviewSvg, string?>(nameof(Color2));
        public static readonly StyledProperty<string?> Color3Property =
            AvaloniaProperty.Register<ThemePreviewSvg, string?>(nameof(Color3));

        public string? SvgPath { get => GetValue(SvgPathProperty); set => SetValue(SvgPathProperty, value); }
        public string? Color1 { get => GetValue(Color1Property); set => SetValue(Color1Property, value); }
        public string? Color2 { get => GetValue(Color2Property); set => SetValue(Color2Property, value); }
        public string? Color3 { get => GetValue(Color3Property); set => SetValue(Color3Property, value); }

        private SKSvg? _svg;
        private string? _originalSvgContent;

        static ThemePreviewSvg()
        {
            SvgPathProperty.Changed.AddClassHandler<ThemePreviewSvg>((x, _) => x.Reload());
            Color1Property.Changed.AddClassHandler<ThemePreviewSvg>((x, _) => x.Reload());
            Color2Property.Changed.AddClassHandler<ThemePreviewSvg>((x, _) => x.Reload());
            Color3Property.Changed.AddClassHandler<ThemePreviewSvg>((x, _) => x.Reload());
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            Reload();
        }

        private void Reload()
        {
            LoadOriginalSvg();
            LoadSvg();
            InvalidateVisual();
        }

        private void LoadOriginalSvg()
        {
            if (string.IsNullOrWhiteSpace(SvgPath))
            {
                _originalSvgContent = null;
                return;
            }
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(SvgPath));
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
                var modified = ModifySvg(_originalSvgContent);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(modified));
                _svg = new SKSvg();
                _svg.Load(stream);
            }
            catch
            {
                _svg = null;
            }
        }

        private string ModifySvg(string original)
        {
            try
            {
                var doc = XDocument.Parse(original);
                // Find drawable path elements; skip the first background path
                var paths = doc.Descendants().Where(e => e.Name.LocalName == "path").ToList();
                if (paths.Count > 0 && !string.IsNullOrWhiteSpace(Color1))
                {
                    paths[0].SetAttributeValue("fill", Color1);
                }
                if (paths.Count > 1 && !string.IsNullOrWhiteSpace(Color2))
                {
                    paths[1].SetAttributeValue("fill", Color2);
                }
                if (paths.Count > 2 && !string.IsNullOrWhiteSpace(Color3))
                {
                    paths[2].SetAttributeValue("fill", Color3);
                }
                return doc.ToString();
            }
            catch
            {
                return original;
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

            var svgBounds = _svg.Picture.CullRect;
            var scaleX = width / svgBounds.Width;
            var scaleY = height / svgBounds.Height;
            var scale = Math.Min(scaleX, scaleY);
            canvas.Scale(scale);
            canvas.DrawPicture(_svg.Picture);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            context.DrawImage(bitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }
    }
}


