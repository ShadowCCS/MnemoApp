using System;
using System.Collections.Generic;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class HBox : Box
{
    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var list = new List<(Box, double, double)>(Children.Count);
        var currentX = x;
        foreach (var child in Children)
        {
            list.Add((child, currentX, baselineY - child.Shift));
            currentX += child.Width;
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
        Height = 0;
        Depth = 0;

        foreach (var child in Children)
        {
            Width += child.Width;
            var effectiveHeight = child.Height + Math.Max(0, child.Shift);
            var effectiveDepth = child.Depth + Math.Max(0, -child.Shift);
            Height = Math.Max(Height, effectiveHeight);
            Depth = Math.Max(Depth, effectiveDepth);
        }
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var currentX = x;
        foreach (var child in Children)
        {
            ctx.RenderChild(child, currentX, baselineY - child.Shift);
            currentX += child.Width;
        }
    }
}