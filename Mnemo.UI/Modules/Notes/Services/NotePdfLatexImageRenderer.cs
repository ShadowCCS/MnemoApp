using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mnemo.Core.Services;
using Mnemo.UI.Controls;

namespace Mnemo.UI.Modules.Notes.Services;

public sealed class NotePdfLatexImageRenderer : INotePdfLatexImageRenderer
{
    /// <summary>Supersample raster (Mindmap-style): same logical DIP box, more pixels for PDF downscaling.</summary>
    private const double BitmapScale = 3.0;

    private readonly ILaTeXEngine _latexEngine;

    public NotePdfLatexImageRenderer(ILaTeXEngine latexEngine)
    {
        _latexEngine = latexEngine;
    }

    public async Task<NotePdfLatexRaster?> RenderLatexPngAsync(string latex, double fontSize, bool inline, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return null;

        var layoutFont = Math.Clamp(fontSize, 8, 72);
        var result = await _latexEngine.BuildLayoutAsync(latex, layoutFont, cancellationToken).ConfigureAwait(true);
        if (result is not LaTeXRenderer renderer)
            return null;

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            renderer.IsInlineMode = inline;
            renderer.Foreground = Brushes.Black;
            renderer.HorizontalAlignment = HorizontalAlignment.Left;
            renderer.VerticalAlignment = VerticalAlignment.Top;

            var desired = renderer.GetDesiredSize();
            var pad = inline ? 8.0 : 16.0;
            var logicalW = Math.Max(1, desired.Width + pad);
            var logicalH = Math.Max(1, desired.Height + pad);
            renderer.Width = logicalW;
            renderer.Height = logicalH;
            renderer.Measure(new Size(logicalW, logicalH));
            renderer.Arrange(new Rect(0, 0, logicalW, logicalH));

            var host = new Border
            {
                Background = Brushes.White,
                Child = renderer,
                Width = logicalW,
                Height = logicalH
            };
            host.Measure(new Size(logicalW, logicalH));
            host.Arrange(new Rect(0, 0, logicalW, logicalH));

            var pw = Math.Max(1, (int)Math.Ceiling(logicalW * BitmapScale));
            var ph = Math.Max(1, (int)Math.Ceiling(logicalH * BitmapScale));
            var dpi = new Vector(96 * BitmapScale, 96 * BitmapScale);

            using var bitmap = new RenderTargetBitmap(new PixelSize(pw, ph), dpi);
            bitmap.Render(host);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            var png = stream.ToArray();
            if (png.Length == 0)
                return null;

            var widthPt = (float)(logicalW * 72.0 / 96.0);
            var heightPt = (float)(logicalH * 72.0 / 96.0);
            return new NotePdfLatexRaster(png, widthPt, heightPt);
        });
    }
}
