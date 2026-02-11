using System;
using System.Collections.Generic;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class HBox : Box
{
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
            
            // Positive shift moves up (increases height above baseline)
            // Negative shift moves down (increases depth below baseline)
            var effectiveHeight = child.Height + Math.Max(0, child.Shift);
            var effectiveDepth = child.Depth + Math.Max(0, -child.Shift);
            
            Height = Math.Max(Height, effectiveHeight);
            Depth = Math.Max(Depth, effectiveDepth);
        }
    }
}