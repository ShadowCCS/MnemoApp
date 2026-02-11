using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mnemo.Infrastructure.Services.LaTeX;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using System;

namespace Mnemo.UI.Controls;

public class LaTeXRenderer : Control
{
    public static readonly StyledProperty<Box?> LayoutProperty =
        AvaloniaProperty.Register<LaTeXRenderer, Box?>(nameof(Layout));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<LaTeXRenderer, IBrush?>(nameof(Foreground));

    private readonly LRUCache<(string, double, uint), FormattedText> _formattedTextCache = new(500);

    private const double DefaultPadding = 16;
    private const double VerticalBuffer = 8;
    private const double DelimiterOffset = 15;
    private const double DelimiterThickness = 1.5;

    public Box? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static LaTeXRenderer()
    {
        AffectsRender<LaTeXRenderer>(LayoutProperty, ForegroundProperty);
        AffectsMeasure<LaTeXRenderer>(LayoutProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == LayoutProperty || change.Property == ForegroundProperty)
        {
            _formattedTextCache.Clear();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }
        
        _formattedTextCache.Clear();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _formattedTextCache.Clear();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout == null)
            return new Size(0, 0);

        // Increase padding and buffer significantly to test if it resolves cropping
        const double extraSafety = 10.0;
        var totalHeight = Layout.Height + Layout.Depth + (DefaultPadding * 2) + VerticalBuffer + extraSafety;
        var totalWidth = Layout.Width + (DefaultPadding * 2) + extraSafety;
        
        return new Size(totalWidth, totalHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Layout == null)
            return;

        // Ensure the baseline is centered within the padded area
        var baseline = Layout.Height + DefaultPadding + (VerticalBuffer / 2) + 5.0; // Extra 5px offset
        RenderBox(context, Layout, DefaultPadding + 5.0, baseline);
    }

    private void RenderBox(DrawingContext context, Box box, double x, double baseline)
    {
        switch (box)
        {
            case CharBox charBox:
                RenderChar(context, charBox, x, baseline + charBox.Shift);
                break;

            case HBox hbox:
                var currentX = x;
                foreach (var child in hbox.Children)
                {
                    RenderBox(context, child, currentX, baseline - child.Shift);
                    currentX += child.Width;
                }
                break;

            case VBox vbox:
                var currentY = baseline - box.Height;
                foreach (var child in vbox.Children)
                {
                    RenderBox(context, child, x, currentY + child.Height);
                    currentY += child.TotalHeight;
                }
                break;

            case FractionBox fracBox:
                var numX = x + (box.Width - fracBox.Numerator.Width) / 2;
                var numY = baseline - fracBox.NumeratorSpacing - fracBox.RuleThickness / 2;
                RenderBox(context, fracBox.Numerator, numX, numY - fracBox.Numerator.Shift);

                var lineY = baseline - fracBox.RuleThickness / 2;
                var pen = new Pen(GetTextBrush(), fracBox.RuleThickness);
                context.DrawLine(pen, new Point(x, lineY), new Point(x + box.Width, lineY));

                var denomX = x + (box.Width - fracBox.Denominator.Width) / 2;
                var denomY = baseline + fracBox.DenominatorSpacing + fracBox.RuleThickness / 2;
                RenderBox(context, fracBox.Denominator, denomX, denomY - fracBox.Denominator.Shift);
                break;

            case ScriptBox scriptBox:
                RenderBox(context, scriptBox.Base, x, baseline);
                var scriptX = x + scriptBox.Base.Width;

                if (scriptBox.Superscript != null)
                {
                    RenderBox(context, scriptBox.Superscript, scriptX, baseline - scriptBox.Superscript.Shift);
                }

                if (scriptBox.Subscript != null)
                {
                    RenderBox(context, scriptBox.Subscript, scriptX, baseline - scriptBox.Subscript.Shift);
                }
                break;

            case SqrtBox sqrtBox:
                var contentTop = baseline - sqrtBox.Content.Height - sqrtBox.Content.Shift;
                var contentBottom = baseline + sqrtBox.Content.Depth - sqrtBox.Content.Shift;
                var overlineY = baseline - sqrtBox.Height + sqrtBox.RuleThickness / 2;
                var checkWidth = sqrtBox.SymbolWidth * 0.3;
                var kneeWidth = sqrtBox.SymbolWidth * 0.35;
                
                var sqrtPath = new PathGeometry();
                var sqrtFigure = new PathFigure { 
                    StartPoint = new Point(x, contentTop * 0.6 + contentBottom * 0.4),
                    IsClosed = false
                };
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + checkWidth, contentBottom) 
                });
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + checkWidth + kneeWidth, overlineY) 
                });
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + box.Width, overlineY) 
                });
                sqrtPath.Figures!.Add(sqrtFigure);

                context.DrawGeometry(null, new Pen(GetTextBrush(), sqrtBox.RuleThickness), sqrtPath);

                RenderBox(context, sqrtBox.Content, x + sqrtBox.SymbolWidth, baseline - sqrtBox.Content.Shift);
                break;

            case SpaceBox:
                break;

            case RuleBox ruleBox:
                var rulePen = new Pen(GetTextBrush(), ruleBox.Height + ruleBox.Depth);
                context.DrawLine(rulePen, new Point(x, baseline), new Point(x + ruleBox.Width, baseline));
                break;

            case MatrixBox matrixBox:
                RenderMatrix(context, matrixBox, x, baseline);
                break;
        }
    }

    private void RenderMatrix(DrawingContext context, MatrixBox matrixBox, double x, double baseline)
    {
        var brush = GetTextBrush();
        var delimiterOffset = 0.0;

        if (matrixBox.MatrixType != "matrix")
        {
            delimiterOffset = DelimiterOffset;
            var delimHeight = (matrixBox.Height + matrixBox.Depth) * 0.9;
            var delimY = baseline - matrixBox.Height + (matrixBox.Height + matrixBox.Depth - delimHeight) / 2;
            DrawDelimiter(context, matrixBox.MatrixType, x + 2, delimY, delimHeight, true, brush);
        }

        var currentY = baseline - matrixBox.Height + 4;
        for (int rowIdx = 0; rowIdx < matrixBox.Cells.Count; rowIdx++)
        {
            var row = matrixBox.Cells[rowIdx];
            var rowHeight = matrixBox.RowHeights[rowIdx];
            var rowDepth = matrixBox.RowDepths[rowIdx];
            var rowBaseline = currentY + rowHeight;

            var currentX = x + delimiterOffset;
            for (int colIdx = 0; colIdx < row.Count; colIdx++)
            {
                var cell = row[colIdx];
                var colWidth = matrixBox.ColumnWidths[colIdx];
                
                var cellX = currentX + (colWidth - cell.Width) / 2;
                RenderBox(context, cell, cellX, rowBaseline);

                currentX += colWidth + matrixBox.CellPadding * 2;
            }

            currentY += rowHeight + rowDepth + matrixBox.RowSpacing;
        }

        if (matrixBox.MatrixType != "matrix")
        {
            var delimHeight = (matrixBox.Height + matrixBox.Depth) * 0.9;
            var delimY = baseline - matrixBox.Height + (matrixBox.Height + matrixBox.Depth - delimHeight) / 2;
            DrawDelimiter(context, matrixBox.MatrixType, x + matrixBox.Width - delimiterOffset + 5, 
                         delimY, delimHeight, false, brush);
        }
    }

    private void DrawDelimiter(DrawingContext context, string matrixType, double x, double y, double height, bool isLeft, IBrush brush)
    {
        var pen = new Pen(brush, DelimiterThickness);
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(x, y), IsClosed = false };

        switch (matrixType)
        {
            case "pmatrix":
                if (isLeft)
                {
                    var controlPoint1 = new Point(x - 3, y + height * 0.2);
                    var controlPoint2 = new Point(x - 3, y + height * 0.8);
                    figure.Segments!.Add(new BezierSegment { Point1 = controlPoint1, Point2 = controlPoint2, Point3 = new Point(x, y + height) });
                }
                else
                {
                    var controlPoint1 = new Point(x + 3, y + height * 0.2);
                    var controlPoint2 = new Point(x + 3, y + height * 0.8);
                    figure.Segments!.Add(new BezierSegment { Point1 = controlPoint1, Point2 = controlPoint2, Point3 = new Point(x, y + height) });
                }
                break;

            case "bmatrix":
                if (isLeft)
                {
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y + height) });
                }
                else
                {
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y + height) });
                }
                break;

            case "vmatrix":
            case "Vmatrix":
                figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                break;
        }

        path.Figures!.Add(figure);
        context.DrawGeometry(null, pen, path);
    }

    private void RenderChar(DrawingContext context, CharBox charBox, double x, double baseline)
    {
        var typeface = Mnemo.UI.Services.LaTeX.Metrics.FontMetrics.Instance.Typeface;
        
        var foreground = GetTextBrush();
        var color = (foreground as ISolidColorBrush)?.Color.ToUInt32() ?? 0xFF000000;

        var cacheKey = (charBox.Character, charBox.FontSize, color);
        if (!_formattedTextCache.TryGetValue(cacheKey, out var formattedText))
        {
            formattedText = new FormattedText(
                charBox.Character,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                charBox.FontSize,
                foreground
            );

            _formattedTextCache.Add(cacheKey, formattedText);
        }

        context.DrawText(formattedText, new Point(x, baseline - formattedText.Baseline));
    }

    private Typeface CreateTypeface()
    {
        var fontFamily = Application.Current?.TryFindResource("MathFontFamily", out var res) == true && res is FontFamily ff
            ? ff
            : new FontFamily("STIX Two Math");
        return new Typeface(fontFamily);
    }

    private IBrush GetTextBrush()
    {
        if (Foreground != null)
            return Foreground;

        if (this.TryFindResource("TextPrimaryBrush", out var resource) && resource is IBrush brush)
        {
            return brush;
        }
        
        return Brushes.Black;
    }
}