using Avalonia;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public class EdgePreviewViewModel
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    public Point StartPoint => new Point(X1, Y1);
    public Point EndPoint => new Point(X2, Y2);
}
