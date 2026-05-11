using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mnemo.UI.Controls;

public sealed class SketchSvgView : Control
{
    public static readonly StyledProperty<string?> SvgProperty =
        AvaloniaProperty.Register<SketchSvgView, string?>(nameof(Svg));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<SketchSvgView, double>(nameof(Zoom), 1);

    public static readonly StyledProperty<double> PanXProperty =
        AvaloniaProperty.Register<SketchSvgView, double>(nameof(PanX));

    public static readonly StyledProperty<double> PanYProperty =
        AvaloniaProperty.Register<SketchSvgView, double>(nameof(PanY));

    private ParsedSketchSvg? _parsedSvg;
    private string? _loadedSvg;

    static SketchSvgView()
    {
        SvgProperty.Changed.AddClassHandler<SketchSvgView>((view, _) =>
        {
            view.ResetPicture();
            view.InvalidateMeasure();
            view.InvalidateVisual();
        });
        ZoomProperty.Changed.AddClassHandler<SketchSvgView>((view, _) => view.InvalidateVisual());
        PanXProperty.Changed.AddClassHandler<SketchSvgView>((view, _) => view.InvalidateVisual());
        PanYProperty.Changed.AddClassHandler<SketchSvgView>((view, _) => view.InvalidateVisual());
    }

    public string? Svg
    {
        get => GetValue(SvgProperty);
        set => SetValue(SvgProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, 0.1, 8));
    }

    public double PanX
    {
        get => GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    public double PanY
    {
        get => GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    public void ZoomAround(double requestedZoom, Point anchor)
    {
        EnsureParsedSvg();
        if (_parsedSvg == null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            Zoom = requestedZoom;
            return;
        }

        var previousZoom = Zoom;
        var nextZoom = Math.Clamp(requestedZoom, 0.1, 8);
        if (Math.Abs(previousZoom - nextZoom) < 0.001)
            return;

        var previousMetrics = CalculateRenderMetrics(_parsedSvg, previousZoom);
        var diagramPoint = new Point(
            (anchor.X - previousMetrics.OffsetX) / previousMetrics.Scale,
            (anchor.Y - previousMetrics.OffsetY) / previousMetrics.Scale);

        var nextMetrics = CalculateRenderMetrics(_parsedSvg, nextZoom);
        PanX = anchor.X - diagramPoint.X * nextMetrics.Scale - nextMetrics.BaseOffsetX;
        PanY = anchor.Y - diagramPoint.Y * nextMetrics.Scale - nextMetrics.BaseOffsetY;
        Zoom = nextZoom;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || string.IsNullOrWhiteSpace(Svg))
            return;

        EnsureParsedSvg();
        if (_parsedSvg == null)
            return;

        RenderParsedSvg(context, _parsedSvg);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureParsedSvg();
        if (_parsedSvg == null)
            return new Size(320, 160);

        return new Size(Math.Max(1, _parsedSvg.Width), Math.Max(1, _parsedSvg.Height));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResetPicture();
        base.OnDetachedFromVisualTree(e);
    }

    private void EnsureParsedSvg()
    {
        if (_loadedSvg == Svg && _parsedSvg != null)
            return;

        ResetPicture();
        if (string.IsNullOrWhiteSpace(Svg))
            return;

        _parsedSvg = ParseSketchSvg(Svg);
        _loadedSvg = Svg;
    }

    private void ResetPicture()
    {
        _parsedSvg = null;
        _loadedSvg = null;
    }

    private void RenderParsedSvg(DrawingContext context, ParsedSketchSvg svg)
    {
        if (svg.Width <= 0 || svg.Height <= 0)
            return;

        var metrics = CalculateRenderMetrics(svg, Zoom);

        using var transform = context.PushTransform(
            Matrix.CreateScale(metrics.Scale, metrics.Scale)
            * Matrix.CreateTranslation(metrics.OffsetX, metrics.OffsetY));

        foreach (var line in svg.Lines)
        {
            var pen = new Pen(ParseBrush(line.Stroke), line.StrokeThickness);
            var start = new Point(line.X1, line.Y1);
            var end = new Point(line.X2, line.Y2);
            context.DrawLine(pen, start, end);
            DrawArrowHead(context, start, end, ParseBrush(line.Stroke));
        }

        foreach (var rect in svg.Rects)
        {
            var fill = ParseBrush(rect.Fill);
            var pen = new Pen(ParseBrush(rect.Stroke), rect.StrokeThickness);
            context.DrawRectangle(fill, pen, new Rect(rect.X, rect.Y, rect.Width, rect.Height), rect.Radius, rect.Radius);
        }

        foreach (var text in svg.Texts)
            DrawSvgText(context, text);
    }

    private RenderMetrics CalculateRenderMetrics(ParsedSketchSvg svg, double zoom)
    {
        var fitScale = Math.Min(Bounds.Width / svg.Width, Bounds.Height / svg.Height);
        var scale = fitScale * Math.Clamp(zoom, 0.1, 8);
        var baseOffsetX = (Bounds.Width - svg.Width * scale) / 2;
        var baseOffsetY = (Bounds.Height - svg.Height * scale) / 2;
        return new RenderMetrics(scale, baseOffsetX, baseOffsetY, baseOffsetX + PanX, baseOffsetY + PanY);
    }

    private static void DrawArrowHead(DrawingContext context, Point start, Point end, IBrush fill)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.1)
            return;

        var ux = dx / length;
        var uy = dy / length;
        var size = 9.0;
        var halfWidth = 4.5;
        var tip = end;
        var baseCenter = new Point(end.X - ux * size, end.Y - uy * size);
        var normal = new Vector(-uy, ux);
        var p1 = baseCenter + normal * halfWidth;
        var p2 = baseCenter - normal * halfWidth;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(p1);
            ctx.LineTo(p2);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);
    }

    private static void DrawSvgText(DrawingContext context, SvgText text)
    {
        var typeface = new Typeface(new FontFamily("Inter,Segoe UI,sans-serif"));
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            text.FontSize,
            ParseBrush(text.Fill));

        var x = text.Anchor == "middle" ? text.X - formatted.Width / 2 : text.X;
        context.DrawText(formatted, new Point(x, text.Y - formatted.Baseline));
    }

    private static ParsedSketchSvg ParseSketchSvg(string svg)
    {
        try
        {
            var document = XDocument.Parse(svg);
            var root = document.Root;
            if (root == null)
                return ParsedSketchSvg.Empty;

            var width = ReadDouble(root, "width", 320);
            var height = ReadDouble(root, "height", 160);
            var rects = new List<SvgRect>();
            var lines = new List<SvgLine>();
            var texts = new List<SvgText>();

            foreach (var element in root.Descendants())
            {
                switch (element.Name.LocalName)
                {
                    case "rect":
                        if (ReadAttribute(element, "width") == "100%")
                            break;
                        rects.Add(new SvgRect(
                            ReadDouble(element, "x", 0),
                            ReadDouble(element, "y", 0),
                            ReadDouble(element, "width", 0),
                            ReadDouble(element, "height", 0),
                            ReadDouble(element, "rx", 0),
                            ReadAttribute(element, "fill") ?? "#00000000",
                            ReadAttribute(element, "stroke") ?? "#000000",
                            ReadDouble(element, "stroke-width", 1)));
                        break;
                    case "line":
                        lines.Add(new SvgLine(
                            ReadDouble(element, "x1", 0),
                            ReadDouble(element, "y1", 0),
                            ReadDouble(element, "x2", 0),
                            ReadDouble(element, "y2", 0),
                            ReadAttribute(element, "stroke") ?? "#000000",
                            ReadDouble(element, "stroke-width", 1)));
                        break;
                    case "text":
                        texts.Add(new SvgText(
                            ReadDouble(element, "x", 0),
                            ReadDouble(element, "y", 0),
                            WebUtility.HtmlDecode(element.Value),
                            ReadDouble(element, "font-size", 12),
                            ReadAttribute(element, "fill") ?? "#000000",
                            ReadAttribute(element, "text-anchor") ?? "start"));
                        break;
                }
            }

            return new ParsedSketchSvg(width, height, rects, lines, texts);
        }
        catch
        {
            return ParsedSketchSvg.Empty;
        }
    }

    private static string? ReadAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private static double ReadDouble(XElement element, string name, double fallback)
    {
        var raw = ReadAttribute(element, name);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        raw = raw.TrimEnd('%');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static IBrush ParseBrush(string value)
    {
        if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            return Brushes.Transparent;
        return Color.TryParse(value, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;
    }

    private sealed record ParsedSketchSvg(
        double Width,
        double Height,
        IReadOnlyList<SvgRect> Rects,
        IReadOnlyList<SvgLine> Lines,
        IReadOnlyList<SvgText> Texts)
    {
        public static readonly ParsedSketchSvg Empty = new(320, 160, [], [], []);
    }

    private sealed record SvgRect(
        double X,
        double Y,
        double Width,
        double Height,
        double Radius,
        string Fill,
        string Stroke,
        double StrokeThickness);

    private sealed record SvgLine(
        double X1,
        double Y1,
        double X2,
        double Y2,
        string Stroke,
        double StrokeThickness);

    private sealed record SvgText(
        double X,
        double Y,
        string Text,
        double FontSize,
        string Fill,
        string Anchor);

    private readonly record struct RenderMetrics(
        double Scale,
        double BaseOffsetX,
        double BaseOffsetY,
        double OffsetX,
        double OffsetY);
}
