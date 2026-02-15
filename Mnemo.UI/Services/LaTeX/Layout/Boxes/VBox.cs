using System;
using System.Collections.Generic;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class VBox : Box
{
    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var list = new List<(Box, double, double)>(Children.Count);
        var currentY = baselineY - Height;
        foreach (var child in Children)
        {
            var childBaseline = currentY + child.Height;
            list.Add((child, x, childBaseline));
            currentY += child.TotalHeight;
        }
        return list;
    }

    public List<Box> Children { get; } = new();

    public void Add(Box box)
    {
        Children.Add(box);
        RecalculateMetrics();
    }

    private void RecalculateMetrics()
    {
        Width = 0;
        double totalHeight = 0;

        foreach (var child in Children)
        {
            Width = Math.Max(Width, child.Width);
            totalHeight += child.TotalHeight;
        }

        Height = totalHeight / 2;
        Depth = totalHeight / 2;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var currentY = baselineY - Height;
        foreach (var child in Children)
        {
            var childBaseline = currentY + child.Height;
            ctx.RenderChild(child, x, childBaseline);
            currentY += child.TotalHeight;
        }
    }
}