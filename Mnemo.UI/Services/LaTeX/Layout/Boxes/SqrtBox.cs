using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class SqrtBox : Box
{
    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var contentBaselineY = baselineY - Content.Shift;
        return [(Content, x + SymbolWidth, contentBaselineY)];
    }

    public Box Content { get; }
    public double SymbolWidth { get; }
    public double RuleThickness { get; }
    public double Padding { get; }

    private const double VerticalBuffer = 2.0;

    public SqrtBox(Box content, double symbolWidth, double ruleThickness, double padding)
    {
        Content = content;
        SymbolWidth = symbolWidth;
        RuleThickness = ruleThickness;
        Padding = padding;

        Width = symbolWidth + content.Width + padding;
        Height = content.Height + padding + ruleThickness + VerticalBuffer;
        Depth = Math.Max(content.Depth, padding) + VerticalBuffer;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var contentBaselineY = baselineY - Content.Shift;
        var contentTop = contentBaselineY - Content.Height;
        var contentBottom = contentBaselineY + Content.Depth;

        var overlineY = baselineY - Height + RuleThickness / 2;
        var checkWidth = Math.Max(2.0, SymbolWidth * 0.35);
        var kneeWidth = Math.Max(3.0, SymbolWidth * 0.4);
        var penThickness = Math.Max(1.5, RuleThickness * 1.5);

        var startY = contentTop * 0.5 + contentBottom * 0.5;
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(x, startY), IsClosed = false };
        figure.Segments!.Add(new LineSegment { Point = new Point(x + checkWidth, contentBottom) });
        figure.Segments!.Add(new LineSegment { Point = new Point(x + checkWidth + kneeWidth, overlineY) });
        figure.Segments!.Add(new LineSegment { Point = new Point(x + Width, overlineY) });
        path.Figures!.Add(figure);

        ctx.DrawPath(path, penThickness);
        ctx.RenderChild(Content, x + SymbolWidth, contentBaselineY);
    }
}
