using System.Collections.Generic;

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
        Height = 0;
        Depth = 0;

        foreach (var child in Children)
        {
            if (child.Width > Width)
                Width = child.Width;
            Height += child.TotalHeight;
        }
    }
}

