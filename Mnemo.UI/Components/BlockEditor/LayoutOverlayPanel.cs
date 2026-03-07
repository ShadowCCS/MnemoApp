using Avalonia;
using Avalonia.Controls;

namespace Mnemo.UI.Components.BlockEditor;

/// <summary>
/// Panel that takes no layout space (reports 0,0 desired size) but arranges its single child
/// at Canvas.Left/Top with the child's Width/Height. Used so overlay visuals
/// (e.g. selection box) do not affect the size of the same Grid cell as the main content.
/// </summary>
public class LayoutOverlayPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            var w = child.Width;
            var h = child.Height;
            var measureSize = new Size(
                double.IsNaN(w) || w <= 0 ? 0 : w,
                double.IsNaN(h) || h <= 0 ? 0 : h);
            child.Measure(measureSize);
        }
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var x = Canvas.GetLeft(child);
            var y = Canvas.GetTop(child);
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;
            var w = child.Width;
            var h = child.Height;
            if (double.IsNaN(w) || w < 0) w = 0;
            if (double.IsNaN(h) || h < 0) h = 0;
            child.Arrange(new Rect(x, y, w, h));
        }
        return finalSize;
    }
}
