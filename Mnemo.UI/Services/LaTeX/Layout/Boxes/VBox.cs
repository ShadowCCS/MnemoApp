using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class VBox : Box
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
        double totalHeight = 0;

        foreach (var child in Children)
        {
            Width = Math.Max(Width, child.Width);
            totalHeight += child.TotalHeight;
        }

        Height = totalHeight; 
        Depth = 0;
    }
}
