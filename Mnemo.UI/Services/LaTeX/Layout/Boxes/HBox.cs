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
            var childHeight = child.Height + child.Shift;
            if (childHeight > Height)
                Height = childHeight;
            var childDepth = child.Depth - child.Shift;
            if (childDepth > Depth)
                Depth = childDepth;
        }
    }
}