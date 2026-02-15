using Avalonia;
using Avalonia.Media;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

/// <summary>
/// Renders a scaled delimiter (e.g. \left( \right)) as a path with fixed stroke thickness
/// so the parenthesis stays thin instead of scaling with font size.
/// </summary>
public class ScaledDelimiterBox : Box
{
    public string Character { get; }
    public double StrokeThickness { get; }

    public ScaledDelimiterBox(string character, double width, double height, double depth, double strokeThickness)
    {
        Character = character;
        Width = width;
        Height = height;
        Depth = depth;
        StrokeThickness = strokeThickness;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var totalH = Height + Depth;
        var topY = baselineY - Height;

        if (Character == "(")
        {
            // Left paren: opening faces right (content). Ends on left at x, bulge left so curve opens right.
            var bulge = totalH * 0.35;
            var path = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(x + bulge, topY),
                IsClosed = false
            };
            figure.Segments!.Add(new BezierSegment
            {
                Point1 = new Point(x, topY),
                Point2 = new Point(x, baselineY + Depth),
                Point3 = new Point(x + bulge, baselineY + Depth)
            });
            path.Figures!.Add(figure);
            ctx.DrawPath(path, StrokeThickness);
        }
        else if (Character == ")")
        {
            // Right paren: opening faces left (content). Ends on right at rx, bulge right so curve opens left.
            var bulge = totalH * 0.35;
            var rx = x + Width;
            var path = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(rx - bulge, topY),
                IsClosed = false
            };
            figure.Segments!.Add(new BezierSegment
            {
                Point1 = new Point(rx, topY),
                Point2 = new Point(rx, baselineY + Depth),
                Point3 = new Point(rx - bulge, baselineY + Depth)
            });
            path.Figures!.Add(figure);
            ctx.DrawPath(path, StrokeThickness);
        }
    }
}
