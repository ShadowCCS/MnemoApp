using System;
using System.IO;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Mnemo.UI.Services;

/// <summary>
/// Captures a visual (e.g. application window) to a PNG file.
/// Must be called from the UI thread.
/// </summary>
public static class ScreenshotService
{
    private const double DefaultDpi = 96;

    /// <summary>
    /// Renders the given visual to a temporary PNG file and returns its path.
    /// </summary>
    /// <param name="visual">The root visual to capture (e.g. TopLevel or Window).</param>
    /// <returns>Path to the temporary PNG file, or null if capture failed.</returns>
    public static string? CaptureToTempFile(Visual visual)
    {
        if (visual == null)
            return null;

        var width = visual.Bounds.Width;
        var height = visual.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            width = 1920;
            height = 1080;
        }

        var pixelSize = new PixelSize((int)Math.Ceiling(width), (int)Math.Ceiling(height));
        var size = new Size(width, height);
        var dpi = new Vector(DefaultDpi, DefaultDpi);

        try
        {
            if (visual is Layoutable layoutable)
            {
                layoutable.Measure(size);
                layoutable.Arrange(new Rect(size));
            }

            using var bitmap = new RenderTargetBitmap(pixelSize, dpi);
            bitmap.Render(visual);

            var path = Path.Combine(Path.GetTempPath(), $"Mnemo_screenshot_{Guid.NewGuid():N}.png");
            using (var stream = File.Create(path))
            {
                bitmap.Save(stream);
            }

            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
