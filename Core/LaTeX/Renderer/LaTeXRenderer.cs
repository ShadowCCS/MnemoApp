using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using MnemoApp.Core.LaTeX.Layout;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.Core.LaTeX.Renderer;

public class LaTeXRenderer : Control
{
    public static readonly StyledProperty<Box?> LayoutProperty =
        AvaloniaProperty.Register<LaTeXRenderer, Box?>(nameof(Layout));

    // Cache commonly used objects to reduce allocations
    private readonly Dictionary<(string, double), FormattedText> _formattedTextCache = new();
    private Typeface? _cachedTypeface;
    private IBrush? _cachedBrush;

    // Add constants
    private const double DefaultPadding = 8;
    private const double DelimiterOffset = 15;
    private const double DelimiterThickness = 1.5;

    public Box? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    // Use LRU cache instead of clear-all
    private readonly int _maxCacheSize = 500;

    static LaTeXRenderer()
    {
        AffectsRender<LaTeXRenderer>(LayoutProperty);
        AffectsMeasure<LaTeXRenderer>(LayoutProperty);
    }

    // Add property changed handler
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == LayoutProperty)
        {
            // Clear caches when layout changes
            _formattedTextCache.Clear();
        }
    }

    // Fix brush caching - subscribe to theme changes
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Subscribe to theme changes
        if (Application.Current != null)
        {
            // Invalidate brush cache on theme changes
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _cachedBrush = null;
        _formattedTextCache.Clear();
        InvalidateVisual();
    }

    // Use LRU cache instead of clear-all
    private void AddToCache((string, double) key, FormattedText text)
    {
        if (_formattedTextCache.Count >= _maxCacheSize)
        {
            // Remove oldest entry (or implement proper LRU)
            var firstKey = _formattedTextCache.Keys.First();
            _formattedTextCache.Remove(firstKey);
        }
        _formattedTextCache[key] = text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout == null)
            return new Size(0, 0);

        // Ensure we have enough space for both height and depth
        // The render method uses baseline = Layout.Height + padding, so we need to account for depth below baseline
        var totalHeight = Layout.Height + Layout.Depth + DefaultPadding * 2; // padding above and below
        return new Size(Layout.Width + DefaultPadding * 2, totalHeight);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Layout == null)
            return;

        // Position content with proper padding to prevent clipping
        var baseline = Layout.Height + DefaultPadding; // Content above baseline
        RenderBox(context, Layout, DefaultPadding, baseline);
    }

    private void RenderBox(DrawingContext context, Box box, double x, double baseline)
    {
        switch (box)
        {
            case CharBox charBox:
                RenderChar(context, charBox, x, baseline);
                break;

            case HBox hbox:
                var currentX = x;
                foreach (var child in hbox.Children)
                {
                    RenderBox(context, child, currentX, baseline + child.Shift);
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
                // Render numerator
                var numX = x + (box.Width - fracBox.Numerator.Width) / 2;
                var numY = baseline - fracBox.NumeratorSpacing - fracBox.RuleThickness / 2 - fracBox.Numerator.Depth;
                RenderBox(context, fracBox.Numerator, numX, numY);

                // Render fraction line
                var lineY = baseline - fracBox.RuleThickness / 2;
                var pen = new Pen(GetTextBrush(), fracBox.RuleThickness);
                context.DrawLine(pen, new Point(x, lineY), new Point(x + box.Width, lineY));

                // Render denominator
                var denomX = x + (box.Width - fracBox.Denominator.Width) / 2;
                var denomY = baseline + fracBox.DenominatorSpacing + fracBox.RuleThickness / 2 + fracBox.Denominator.Height;
                RenderBox(context, fracBox.Denominator, denomX, denomY);
                break;

            case ScriptBox scriptBox:
                // Render base
                RenderBox(context, scriptBox.Base, x, baseline);
                var scriptX = x + scriptBox.Base.Width;

                // Render superscript - positioned at the top of the total height
                if (scriptBox.Superscript != null)
                {
                    var supY = baseline - scriptBox.Height + scriptBox.Superscript.Height;
                    RenderBox(context, scriptBox.Superscript, scriptX, supY);
                }

                // Render subscript - positioned at the bottom of the total depth
                if (scriptBox.Subscript != null)
                {
                    var subY = baseline + scriptBox.Depth - scriptBox.Subscript.Depth;
                    RenderBox(context, scriptBox.Subscript, scriptX, subY);
                }
                break;

            case SqrtBox sqrtBox:
                // Calculate positions based on actual box metrics
                var contentTop = baseline - sqrtBox.Content.Height;
                var contentBottom = baseline + sqrtBox.Content.Depth;
                var overlineY = baseline - box.Height;  // Top of the sqrt box
                var checkWidth = sqrtBox.SymbolWidth * 0.3;  // Small check mark part
                var kneeWidth = sqrtBox.SymbolWidth * 0.35;  // Downward angle part
                
                // Draw radical symbol (✓ shape + overline)
                var sqrtPath = new PathGeometry();
                var sqrtFigure = new PathFigure { 
                    StartPoint = new Point(x, contentTop * 0.6 + contentBottom * 0.4),
                    IsClosed = false
                };
                // Down to the lowest point (below baseline if content has depth)
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + checkWidth, contentBottom) 
                });
                // Up to the top (overline level)
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + checkWidth + kneeWidth, overlineY) 
                });
                // Overline across the top
                sqrtFigure.Segments!.Add(new LineSegment { 
                    Point = new Point(x + box.Width, overlineY) 
                });
                sqrtPath.Figures!.Add(sqrtFigure);

                context.DrawGeometry(null, new Pen(GetTextBrush(), 1.5), sqrtPath);

                // Render content
                RenderBox(context, sqrtBox.Content, x + sqrtBox.SymbolWidth, baseline);
                break;

            case SpaceBox:
                // Nothing to render
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

        // Draw left delimiter
        if (matrixBox.MatrixType != "matrix")
        {
            delimiterOffset = DelimiterOffset; // Increased spacing
            var delimHeight = (matrixBox.Height + matrixBox.Depth) * 0.9; // Slightly shorter
            var delimY = baseline - matrixBox.Height + (matrixBox.Height + matrixBox.Depth - delimHeight) / 2;
            DrawDelimiter(context, matrixBox.MatrixType, x + 2, delimY, delimHeight, true, brush);
        }

        // Render matrix cells
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
                
                // Center cell in column
                var cellX = currentX + (colWidth - cell.Width) / 2;
                RenderBox(context, cell, cellX, rowBaseline);

                currentX += colWidth + matrixBox.CellPadding * 2;
            }

            currentY += rowHeight + rowDepth + matrixBox.RowSpacing;
        }

        // Draw right delimiter
        if (matrixBox.MatrixType != "matrix")
        {
            var delimHeight = (matrixBox.Height + matrixBox.Depth) * 0.9; // Slightly shorter
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
                    // Left parenthesis: curved left
                    var controlPoint1 = new Point(x - 3, y + height * 0.2);
                    var controlPoint2 = new Point(x - 3, y + height * 0.8);
                    figure.Segments!.Add(new BezierSegment { Point1 = controlPoint1, Point2 = controlPoint2, Point3 = new Point(x, y + height) });
                }
                else
                {
                    // Right parenthesis: curved right
                    var controlPoint1 = new Point(x + 3, y + height * 0.2);
                    var controlPoint2 = new Point(x + 3, y + height * 0.8);
                    figure.Segments!.Add(new BezierSegment { Point1 = controlPoint1, Point2 = controlPoint2, Point3 = new Point(x, y + height) });
                }
                break;

            case "bmatrix":
                if (isLeft)
                {
                    // Left bracket: proper [ shape
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y + height) });
                }
                else
                {
                    // Right bracket: proper ] shape
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y + height) });
                }
                break;

            case "vmatrix":
            case "Vmatrix":
                // Vertical line
                figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                break;
        }

        path.Figures!.Add(figure);
        context.DrawGeometry(null, pen, path);
    }

    private void RenderChar(DrawingContext context, CharBox charBox, double x, double baseline)
    {
        // Use cached typeface from resource
        if (_cachedTypeface == null)
        {
            try
            {
                var fontFamily = Application.Current?.FindResource("MathFontFamily") as FontFamily;
                _cachedTypeface = new Typeface(fontFamily ?? new FontFamily("STIX Two Math"));
            }
            catch
            {
                _cachedTypeface = new Typeface("STIX Two Math");
            }
        }
        var foreground = GetTextBrush();

        // Try to get cached formatted text
        var cacheKey = (charBox.Character, charBox.FontSize);
        if (!_formattedTextCache.TryGetValue(cacheKey, out var formattedText))
        {
            formattedText = new FormattedText(
                charBox.Character,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _cachedTypeface ?? new Typeface(Application.Current?.FindResource("MathFontFamily") as FontFamily ?? new FontFamily("STIX Two Math")),
                charBox.FontSize,
                foreground
            );

            AddToCache(cacheKey, formattedText);
        }

        context.DrawText(formattedText, new Point(x, baseline - charBox.Height));
    }

    private IBrush GetTextBrush()
    {
        // Cache the brush to avoid repeated lookups
        if (_cachedBrush != null)
            return _cachedBrush;

        // Try to use themed text brush; fallback to black
        if (this.TryFindResource("TextPrimaryBrush", out var resource) && resource is IBrush brush)
        {
            _cachedBrush = brush;
            return brush;
        }
        
        _cachedBrush = Brushes.Black;
        return _cachedBrush;
    }
}

