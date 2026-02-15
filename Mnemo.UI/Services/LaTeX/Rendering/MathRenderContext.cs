using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Mnemo.Infrastructure.Services.LaTeX;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using Mnemo.UI.Services.LaTeX.Metrics;

namespace Mnemo.UI.Services.LaTeX.Rendering;

/// <summary>
/// Renders the math box tree into an Avalonia DrawingContext.
/// Baseline convention: baselineY is the Y coordinate where the box's baseline sits (Y down).
/// </summary>
public sealed class MathRenderContext : IMathRenderContext
{
    private readonly DrawingContext _dc;
    private readonly LRUCache<(string, double, uint), FormattedText> _textCache;

    public MathRenderContext(DrawingContext dc, IBrush? brush, LRUCache<(string, double, uint), FormattedText> textCache)
    {
        _dc = dc;
        _textCache = textCache;
        Brush = brush ?? Brushes.Black;
    }

    public IBrush Brush { get; set; }

    public void RenderChild(Box box, double x, double baselineY)
    {
        box.Render(this, x, baselineY);
    }

    public void DrawText(string character, double fontSize, double x, double baselineY)
    {
        var typeface = Mnemo.UI.Services.LaTeX.Metrics.FontMetrics.Instance.Typeface;
        var color = (Brush as ISolidColorBrush)?.Color.ToUInt32() ?? 0xFF000000;
        var cacheKey = (character, fontSize, color);

        if (!_textCache.TryGetValue(cacheKey, out var formattedText))
        {
            formattedText = new FormattedText(
                character,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brush);
            _textCache.Add(cacheKey, formattedText);
        }

        _dc.DrawText(formattedText, new Point(x, baselineY - formattedText.Baseline));
    }

    public void DrawLine(double x1, double y1, double x2, double y2, double thickness)
    {
        var pen = new Pen(Brush, thickness);
        _dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
    }

    public void DrawPath(PathGeometry path, double thickness)
    {
        _dc.DrawGeometry(null, new Pen(Brush, thickness), path);
    }

    public IBrush GetBrush() => Brush;
}
