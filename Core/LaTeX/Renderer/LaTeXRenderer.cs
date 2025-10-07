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

namespace MnemoApp.Core.LaTeX.Renderer;

public class LaTeXRenderer : Control
{
    public static readonly StyledProperty<Box?> LayoutProperty =
        AvaloniaProperty.Register<LaTeXRenderer, Box?>(nameof(Layout));

    // Cache commonly used objects to reduce allocations
    private readonly Dictionary<(string, double), FormattedText> _formattedTextCache = new();
    private Typeface? _cachedTypeface;
    private IBrush? _cachedBrush;

    public Box? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    static LaTeXRenderer()
    {
        AffectsRender<LaTeXRenderer>(LayoutProperty);
        AffectsMeasure<LaTeXRenderer>(LayoutProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Layout == null)
            return new Size(0, 0);

        // Add more padding to prevent clipping
        // The render method uses baseline = Layout.Height + 2, so we need to account for that
        var padding = 8; // Increased from 4 to 8
        return new Size(Layout.Width + padding, Layout.TotalHeight + padding);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Layout == null)
            return;

        // Center the content within the available space
        var padding = 4;
        var baseline = Layout.Height + padding;
        RenderBox(context, Layout, padding, baseline);
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

                // Render superscript
                if (scriptBox.Superscript != null)
                {
                    var supY = baseline - scriptBox.Base.Height * 0.5 - scriptBox.Superscript.Depth;
                    RenderBox(context, scriptBox.Superscript, scriptX, supY);
                }

                // Render subscript
                if (scriptBox.Subscript != null)
                {
                    var subY = baseline + scriptBox.Base.Height * 0.5 + scriptBox.Subscript.Height;
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
            delimiterOffset = 10;
            var leftDelim = matrixBox.MatrixType switch
            {
                "pmatrix" => "(",
                "bmatrix" => "[",
                "vmatrix" => "|",
                "Vmatrix" => "‖",
                _ => "("
            };

            var typeface = new Typeface(Application.Current?.FindResource("MathFontFamily") as FontFamily ?? new FontFamily("STIX Two Math"));
            var delimSize = matrixBox.Height + matrixBox.Depth;
            var formattedText = new FormattedText(
                leftDelim,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                delimSize * 1.2,
                brush
            );
            context.DrawText(formattedText, new Point(x + 2, baseline - matrixBox.Height));
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
            var rightDelim = matrixBox.MatrixType switch
            {
                "pmatrix" => ")",
                "bmatrix" => "]",
                "vmatrix" => "|",
                "Vmatrix" => "‖",
                _ => ")"
            };

            var typeface = new Typeface(Application.Current?.FindResource("MathFontFamily") as FontFamily ?? new FontFamily("STIX Two Math"));
            var delimSize = matrixBox.Height + matrixBox.Depth;
            var formattedText = new FormattedText(
                rightDelim,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                delimSize * 1.2,
                brush
            );
            context.DrawText(formattedText, new Point(x + matrixBox.Width - delimiterOffset - 2, baseline - matrixBox.Height));
        }
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

            // Limit cache size
            if (_formattedTextCache.Count > 500)
            {
                _formattedTextCache.Clear();
            }
            _formattedTextCache[cacheKey] = formattedText;
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

